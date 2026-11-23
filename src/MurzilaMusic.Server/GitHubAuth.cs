using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MurzilaMusic.Server;

/// <summary>GitHub OAuth: только данные самого пользователя, без доступа к репозиториям.</summary>
public sealed class GitHubAuth
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _clientId;
    private readonly string _secret;
    private readonly string _redirectUri;

    public GitHubAuth(string serverUrl)
    {
        _clientId = Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID") ?? "";
        _secret = Environment.GetEnvironmentVariable("GITHUB_CLIENT_SECRET") ?? "";
        _redirectUri = Environment.GetEnvironmentVariable("GITHUB_REDIRECT_URI")
                       ?? serverUrl.TrimEnd('/') + "/api/auth/github/callback";
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_secret);
    public string RedirectUri => _redirectUri;
    public string Issue => !Configured ? "Не заданы GITHUB_CLIENT_ID и GITHUB_CLIENT_SECRET." : LastError;
    public string LastError { get; private set; } = "";

    public string BuildAuthorizeUrl(string state) =>
        "https://github.com/login/oauth/authorize?client_id=" + Uri.EscapeDataString(_clientId)
        + "&redirect_uri=" + Uri.EscapeDataString(_redirectUri)
        + "&scope=" + Uri.EscapeDataString("user:email")
        + "&state=" + Uri.EscapeDataString(state);

    public async Task<GoogleUser?> ExchangeAsync(string code)
    {
        LastError = "";
        if (!Configured) return null;
        try
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
            tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId, ["client_secret"] = _secret,
                ["code"] = code, ["redirect_uri"] = _redirectUri
            });
            using var tokenResponse = await Client.SendAsync(tokenRequest);
            string tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            using var tokenJson = JsonDocument.Parse(tokenBody);
            string accessToken = tokenJson.RootElement.TryGetProperty("access_token", out var at)
                ? at.GetString() ?? "" : "";
            if (accessToken.Length == 0)
            {
                LastError = "GitHub не вернул access_token: " + Trim(tokenBody, 160);
                return null;
            }

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
            userRequest.Headers.TryAddWithoutValidation("User-Agent", "MurzilaMusic");
            using var userResponse = await Client.SendAsync(userRequest);
            if (!userResponse.IsSuccessStatusCode) return null;
            using var userJson = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync());
            var root = userJson.RootElement;
            string id = root.TryGetProperty("id", out var idProp) ? idProp.GetRawText() : "";
            string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
            string login = root.TryGetProperty("login", out var loginProp) ? loginProp.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name)) name = login;
            string email = root.TryGetProperty("email", out var mailProp) ? mailProp.GetString() ?? "" : "";

            using var emailRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/emails");
            emailRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
            emailRequest.Headers.TryAddWithoutValidation("User-Agent", "MurzilaMusic");
            using var emailResponse = await Client.SendAsync(emailRequest);
            if (emailResponse.IsSuccessStatusCode)
            {
                using var emailJson = JsonDocument.Parse(await emailResponse.Content.ReadAsStringAsync());
                foreach (var item in emailJson.RootElement.EnumerateArray())
                {
                    bool primary = item.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True;
                    bool verified = item.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
                    string found = item.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
                    if (primary && verified && found.Contains('@')) { email = found; break; }
                }
            }
            if (string.IsNullOrWhiteSpace(email)) { LastError = "GitHub не вернул подтверждённую почту."; return null; }
            return new GoogleUser(email.ToLowerInvariant(), name, true, "github:" + id);
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    private static string Trim(string text, int max) =>
        text.Replace("\r", " ").Replace("\n", " ").Length <= max
            ? text.Replace("\r", " ").Replace("\n", " ").Trim()
            : text.Replace("\r", " ").Replace("\n", " ").Trim()[..max] + "…";
}
