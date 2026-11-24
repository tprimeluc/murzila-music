using System.Net.Http;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// Аккаунт через сайт: свой Murzila-аккаунт, GitHub или Google.
/// Сайт остаётся источником логина — браузер только открывает страницу входа
/// и читает статус /api/web/me.
/// </summary>
public sealed class AccountService
{
    private const string Site = "https://murzila-music.onrender.com";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static string[] Providers => new[] { "Murzila", "GitHub", "Google" };

    public string GetAuthUrl(string provider) => provider switch
    {
        "GitHub" => Site + "/api/auth/github/start",
        "Google" => Site + "/api/auth/google/start",
        _ => Site + "/player"
    };

    public async Task<bool> IsSignedInAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(Site + "/api/web/me"));
            return doc.RootElement.TryGetProperty("signedIn", out var value) && value.GetBoolean();
        }
        catch { return false; }
    }
}
