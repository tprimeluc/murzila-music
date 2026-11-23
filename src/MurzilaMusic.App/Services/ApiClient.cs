using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MurzilaMusic.Shared;

namespace MurzilaMusic.App.Services;

/// <summary>Ошибка сервера аккаунтов с понятным текстом для пользователя.</summary>
public sealed class ApiException : Exception
{
    public ApiException(string message, int status) : base(message) => Status = status;

    public int Status { get; }
}

public sealed class ServerInfo
{
    public string PaymentProvider { get; set; } = "";
    public bool RealPayments { get; set; }
    public bool EmailConfigured { get; set; }
    public bool GoogleConfigured { get; set; }
    public string GoogleIssue { get; set; } = "";
    public string GoogleRedirectUri { get; set; } = "";
    public bool SmsConfigured { get; set; }
    public string SmsIssue { get; set; } = "";
    public bool PasswordResetEnabled { get; set; }
    public bool RequireEmailCode { get; set; }
    public string CatalogVersion { get; set; } = "";
    public string StoreProvider { get; set; } = "";
    public string StoreMethod { get; set; } = "";
    public bool TestPayments { get; set; }
    public bool PromoEnabled { get; set; }
    public bool RestoreEnabled { get; set; }
    public bool SelfRefund { get; set; }
    public bool AdminConfigured { get; set; }
}

/// <summary>Ответ на запрос кода: в тестовом режиме сервер возвращает сам код.</summary>
public sealed class ServerCodeResult
{
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string MaskedEmail { get; set; } = "";
    public string MaskedPhone { get; set; } = "";
    public bool TestMode { get; set; }
    public string? TestCode { get; set; }
    public int ExpiresIn { get; set; }
}

public sealed class ServerGoogleStart
{
    public string Url { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed class ServerGoogleStatus
{
    public bool Ready { get; set; }
    public string? Token { get; set; }
    public ServerUser? User { get; set; }
}

public sealed class ServerUser
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Tariff { get; set; }
    public string? Phone { get; set; }
    public bool PhoneVerified { get; set; }
    public bool EmailVerified { get; set; }
    public bool GoogleLinked { get; set; }
}

public sealed class ServerLinkedItem
{
    public string Provider { get; set; } = "";
    public string Value { get; set; } = "";
    public bool Verified { get; set; }
}

public sealed class ServerLinkedResult
{
    public List<ServerLinkedItem> Items { get; set; } = new();
}

public sealed class ServerAuth
{
    public string Token { get; set; } = "";
    public ServerUser User { get; set; } = new();
}

public sealed class ServerPayment
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ConfirmationUrl { get; set; }
    public string Provider { get; set; } = "test";
    public int Tariff { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ServerOrder
{
    public string Id { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductKind { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int AmountMinor { get; set; }
    public string Provider { get; set; } = "";
    public string Method { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ConfirmationUrl { get; set; }
    public string? TrackId { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? PaidAt { get; set; }
    public decimal Amount => AmountMinor / 100m;
}

public sealed class ServerEntitlement
{
    public string Id { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string ProductKind { get; set; } = "";
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public string StartsAt { get; set; } = "";
    public string? ExpiresAt { get; set; }
}

public sealed class ServerRefund
{
    public string Id { get; set; } = "";
    public string OrderId { get; set; } = "";
    public int AmountMinor { get; set; }
    public string ReasonCode { get; set; } = "";
    public string? Comment { get; set; }
    public string Status { get; set; } = "";
    public string RequestedAt { get; set; } = "";
    public string? DecisionNote { get; set; }
    public string? CompletedAt { get; set; }
}

public sealed class ServerAccountState
{
    public ServerUser User { get; set; } = new();
    public string ActivePlanId { get; set; } = "free";
    public string AdsLevel { get; set; } = "full";
    public List<string> Features { get; set; } = new();
    public List<ServerEntitlement> Entitlements { get; set; } = new();
    public List<ServerOrder> Orders { get; set; } = new();
    public List<ServerRefund> Refunds { get; set; } = new();
    public int BalanceMinor { get; set; }
    public List<string> UnlockedTrackIds { get; set; } = new();
    public decimal Balance => BalanceMinor / 100m;
}

public sealed class ServerRestoreResult
{
    public int Restored { get; set; }
    public string ActivePlanId { get; set; } = "free";
    public int BalanceMinor { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Клиент локального сервера аккаунтов и платежей.
/// Обычный пользователь не вводит адрес: приложение всегда подключается к встроенному онлайн-сервису.
/// Переменная MURZILA_SERVER_URL нужна только для разработки и аварийного переключения.
/// </summary>
public static class ApiClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Короткий постоянный публичный адрес бесплатного онлайн-сервиса.</summary>
    public const string DefaultOnlineUrl = "https://murzila-music.shares.zrok.io";

    /// <summary>Запасной адрес: сервер, запущенный на этом же ПК.</summary>
    public const string LocalUrl = "http://localhost:5188";

    private static string? _detectedUrl;

    public static string BaseUrl
    {
        get
        {
            // Явная переменная окружения имеет наивысший приоритет (разработка/аварийный режим).
            string fromEnv = Environment.GetEnvironmentVariable("MURZILA_SERVER_URL") ?? "";
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.TrimEnd('/');
            // Локальный сервер — основной путь. Публичный адрес только запасной:
            // без DNS он недоступен и раньше ломал синхронизацию и API.
            return (_detectedUrl ?? (SettingsService.Current.ServerMode ? LocalUrl : DefaultOnlineUrl))
                .TrimEnd('/');
        }
    }

    /// <summary>
    /// Проверяет публичный адрес, а если туннель недоступен — переключается на локальный сервер.
    /// Вызывается один раз при запуске приложения.
    /// </summary>
    public static async Task DetectServerAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MURZILA_SERVER_URL"))) return;

        // Сначала локальный сервер: он запущен на этом ПК и отвечает мгновенно.
        if (await ProbeAsync(LocalUrl)) { _detectedUrl = LocalUrl; return; }
        if (await ProbeAsync(DefaultOnlineUrl)) { _detectedUrl = DefaultOnlineUrl; return; }
        _detectedUrl = LocalUrl;
    }

    private static async Task<bool> ProbeAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await Client.GetAsync(url.TrimEnd('/') + "/api/info", cts.Token);
            if (!response.IsSuccessStatusCode) return false;
            string text = await response.Content.ReadAsStringAsync(cts.Token);
            return text.Contains("\"catalogVersion\"", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"paymentProvider\"", StringComparison.OrdinalIgnoreCase)
                || text.Contains("\"emailConfigured\"", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static string AbsoluteUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : BaseUrl + (url.StartsWith('/') ? url : "/" + url);

    /// <summary>Быстрая проверка, запущен ли сервер. Ошибки не выбрасываются.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await Client.GetAsync(BaseUrl + "/api/info", timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static Task<ServerInfo> GetInfoAsync() => GetAsync<ServerInfo>("/api/info", null);

    public static Task<ServerAuth> RegisterAsync(string name, string email, string password) =>
        PostAsync<ServerAuth>("/api/auth/register", new { name, email, password }, null);

    public static Task<ServerAuth> LoginAsync(string email, string password) =>
        PostAsync<ServerAuth>("/api/auth/login", new { email, password }, null);

    public static Task<ServerCodeResult> RequestRegisterCodeAsync(string name, string email, string password) =>
        PostAsync<ServerCodeResult>("/api/auth/register/request-code", new { name, email, password }, null);

    public static Task<ServerAuth> VerifyRegisterCodeAsync(string email, string code) =>
        PostAsync<ServerAuth>("/api/auth/register/verify", new { email, code }, null);

    public static Task<ServerCodeResult> RequestLoginCodeAsync(string email, string password) =>
        PostAsync<ServerCodeResult>("/api/auth/login/request-code", new { email, password }, null);

    public static Task<ServerAuth> VerifyLoginCodeAsync(string email, string code) =>
        PostAsync<ServerAuth>("/api/auth/login/verify", new { email, code }, null);

    public static Task<ServerGoogleStart> GoogleStartAsync(bool link = false, string? token = null) =>
        GetAsync<ServerGoogleStart>("/api/auth/google/start" + (link ? "?mode=link" : ""), token);

    public static Task<ServerGoogleStatus> GoogleStatusAsync(string state) =>
        GetAsync<ServerGoogleStatus>("/api/auth/google/status/" + Uri.EscapeDataString(state), null);

    // Восстановление пароля и управление идентичностью.
    public static Task<ServerCodeResult> ForgotRequestCodeAsync(string email) =>
        PostAsync<ServerCodeResult>("/api/auth/forgot/request-code", new { email }, null);

    public static Task<ServerAuth> ResetPasswordAsync(string email, string code, string newPassword) =>
        PostAsync<ServerAuth>("/api/auth/forgot/reset", new { email, code, newPassword }, null);

    public static Task ChangePasswordAsync(string token, string currentPassword, string newPassword) =>
        SendAsync("/api/account/password", HttpMethod.Post, new { currentPassword, newPassword }, token);

    public static Task<ServerCodeResult> ChangeEmailRequestCodeAsync(string token, string newEmail) =>
        PostAsync<ServerCodeResult>("/api/account/email/request-code", new { newEmail }, token);

    public static Task ChangeEmailVerifyAsync(string token, string newEmail, string code) =>
        SendAsync("/api/account/email/verify", HttpMethod.Post, new { newEmail, code }, token);

    public static Task<ServerCodeResult> PhoneRequestCodeAsync(string phone, string? token, string purpose = "bind") =>
        PostAsync<ServerCodeResult>("/api/account/phone/request-code", new { phone, purpose }, token);

    public static Task<ServerAuth> PhoneVerifyAsync(string phone, string code, string? token, string purpose = "bind") =>
        PostAsync<ServerAuth>("/api/account/phone/verify", new { phone, code, purpose }, token);

    public static Task PhoneUnlinkAsync(string token) =>
        SendAsync("/api/account/phone/unlink", HttpMethod.Post, null, token);

    public static Task<ServerLinkedResult> LinkedAsync(string token) =>
        GetAsync<ServerLinkedResult>("/api/account/linked", token);

    public static Task UnlinkAsync(string token, string provider) =>
        SendAsync("/api/account/linked/" + Uri.EscapeDataString(provider) + "/unlink", HttpMethod.Post, null, token);

    public static Task<ServerUser> MeAsync(string token) => GetAsync<ServerUser>("/api/auth/me", token);

    public static Task LogoutAsync(string token) => SendAsync("/api/auth/logout", HttpMethod.Post, null, token);

    public static Task<ServerPayment> CreatePaymentAsync(string token, int tariff, string description, string? trackId) =>
        PostAsync<ServerPayment>("/api/payments/create",
            new { tariff, description, trackId }, token);

    public static Task<ServerPayment> PaymentStatusAsync(string token, string id) =>
        GetAsync<ServerPayment>("/api/payments/status/" + Uri.EscapeDataString(id), token);

    public static Task<CatalogDto> GetCatalogAsync() => GetAsync<CatalogDto>("/api/catalog", null);

    public static Task<ServerAccountState> GetAccountStateAsync(string token) =>
        GetAsync<ServerAccountState>("/api/account/state", token);

    public static Task<ServerOrder> CreateStoreOrderAsync(string token, string productId, string method,
        string? promoCode, string? trackId, string? idempotencyKey) =>
        PostAsync<ServerOrder>("/api/store/orders", new
        {
            productId, method, promoCode, trackId,
            idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey
        }, token);

    public static Task<ServerOrder> StoreOrderStatusAsync(string token, string id) =>
        GetAsync<ServerOrder>("/api/store/orders/" + Uri.EscapeDataString(id), token);

    public static Task<ServerRestoreResult> RestorePurchasesAsync(string token) =>
        PostAsync<ServerRestoreResult>("/api/store/restore", new { }, token);

    public static Task<ServerRefund> CreateRefundAsync(string token, string orderId, string reasonCode, string? comment) =>
        PostAsync<ServerRefund>("/api/store/refunds", new { orderId, reasonCode, comment }, token);

    private static Task<T> GetAsync<T>(string path, string? token) => SendAsync<T>(path, HttpMethod.Get, null, token);

    private static Task<T> PostAsync<T>(string path, object body, string? token) =>
        SendAsync<T>(path, HttpMethod.Post, body, token);

    private static async Task<T> SendAsync<T>(string path, HttpMethod method, object? body, string? token)
    {
        string text = await SendAsync(path, method, body, token);
        return JsonSerializer.Deserialize<T>(text, Json)
               ?? throw new ApiException("Сервер вернул пустой ответ.", 500);
    }

    private static async Task<string> SendAsync(string path, HttpMethod method, object? body, string? token)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Client.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new ApiException(ParseError(text, response), (int)response.StatusCode);
        return text;
    }

    private static string ParseError(string body, HttpResponseMessage response)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                string? message = error.GetString();
                if (!string.IsNullOrWhiteSpace(message)) return message;
            }
        }
        catch { /* сервер мог вернуть HTML */ }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => Txt.ApiUnauthorized,
            System.Net.HttpStatusCode.Conflict => Txt.ApiConflict,
            System.Net.HttpStatusCode.BadRequest => Txt.ApiBadRequest,
            System.Net.HttpStatusCode.ServiceUnavailable => Txt.ApiUnavailable,
            _ => Txt.ApiUnknown((int)response.StatusCode, response.ReasonPhrase ?? "")
        };
    }
}
