using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MurzilaMusic.Server;

public sealed record RegisterRequest(string Name, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record PaymentRequest(int Tariff, string? Description, string? TrackId);
public sealed record WebhookEvent(string? PaymentId, string? Status);
public sealed record RequestCodeRequest(string Name, string Email, string Password);
public sealed record VerifyCodeRequest(string Email, string Code);
public sealed record LoginCodeRequest(string Email, string Password);
public sealed record ForgotRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
public sealed record SetSharedRequest(bool Allowed);
public sealed record TwoFactorVerifyRequest(string Email, string Code);
public sealed record Disable2FaRequest(string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ChangeEmailCodeRequest(string NewEmail);
public sealed record VerifyEmailCodeRequest(string NewEmail, string Code);
public sealed record PhoneRequest(string Phone, string? Purpose);
public sealed record PhoneVerifyRequest(string Phone, string Code, string? Purpose);
public sealed record GoogleStartResult(string Url, string State);
public sealed record GoogleUser(string Email, string Name, bool EmailVerified, string Subject = "");
public sealed record CreateStoreOrderRequest(string ProductId, string? Method, string? PromoCode, string? TrackId, string? IdempotencyKey);
public sealed record CreateRefundRequest(string OrderId, string ReasonCode, string? Comment);
public sealed record AdminPromoRequest(string ProductId, int MaxUses, string? ExpiresAt, string? Note);
public sealed record AdminDecisionRequest(bool Approve, string? Note);
public sealed record AdminGrantRequest(string UserId, int AmountMinor, string Reason);
public sealed record CreateApiAppRequest(string? Name);
public sealed record CreateApiTokenRequest(string ApiKey);

public sealed class UserDto
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

public sealed class AuthResult
{
    public string Token { get; set; } = "";
    public UserDto User { get; set; } = new();
}

public sealed class PaymentDto
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? ConfirmationUrl { get; set; }
    public string Provider { get; set; } = "test";
    public int Tariff { get; set; }
    public decimal Amount { get; set; }
}

public class UserRow
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

public sealed class UserAuthRow : UserRow
{
    public string Salt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class PaymentRow
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Provider { get; set; } = "test";
    public string? ProviderPaymentId { get; set; }
    public int Tariff { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "pending";
    public string? ConfirmationUrl { get; set; }
    public string? TrackId { get; set; }
}

/// <summary>Отправка кодов подтверждения. Без SMTP работает тестовый режим: код показывается в UI.</summary>
public sealed class SmtpMailer
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _password;
    private readonly string _from;

    public SmtpMailer()
    {
        _host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "";
        _port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out int port) ? port : 587;
        _user = Environment.GetEnvironmentVariable("SMTP_USER") ?? "";
        _password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? "";
        _from = Environment.GetEnvironmentVariable("SMTP_FROM") ?? _user;
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_host) && !string.IsNullOrWhiteSpace(_from);

    public static string PurposeTitle(string purpose) => purpose switch
    {
        "register" => "подтверждения регистрации",
        "login" => "входа в Murzila-Music",
        "reset" => "сброса пароля",
        "email" => "смены почты",
        _ => "подтверждения"
    };

    public static string Message(string name, string code, string purpose = "login") =>
        $"Здравствуйте, {name}!\n\n" +
        $"Ваш код для {PurposeTitle(purpose)}: {code}\n\n" +
        "Введите его в приложении Murzila-Music в течение 10 минут. " +
        "Никому не сообщайте этот код.\n\n" +
        "Если это не вы — просто проигнорируйте письмо: ваш пароль не менялся, " +
        "а код перестанет действовать через 10 минут.\n\n" +
        "С уважением, команда MursikT7 Corporation";

    public static string HtmlMessage(string name, string code, string purpose = "login")
    {
        string title = PurposeTitle(purpose);
        return "<!doctype html><html><body style=\"margin:0;padding:24px;background:#14141a;" +
               "font-family:Segoe UI,Arial,sans-serif;color:#edeef3\">" +
               "<div style=\"max-width:520px;margin:auto;background:#1e1e28;border-radius:16px;padding:28px\">" +
               "<div style=\"font-size:13px;color:#9b9ba8\">Murzila-Music</div>" +
               $"<h2 style=\"margin:12px 0 8px\">Здравствуйте, {EscapeHtml(name)}!</h2>" +
               $"<p style=\"color:#c6c6d0;font-size:14px\">Код для {EscapeHtml(title)}. " +
               "Никому не сообщайте его.</p>" +
               "<div style=\"margin:22px 0;padding:18px;border-radius:12px;background:#262630;" +
               $"text-align:center;font:700 34px Consolas,monospace;letter-spacing:8px;color:#7ee081\">{code}</div>" +
               "<p style=\"color:#9b9ba8;font-size:13px\">Код действует 10 минут. " +
               "Если это не вы — просто проигнорируйте письмо: пароль не менялся.</p>" +
               "<p style=\"color:#6f6f7c;font-size:12px;margin-top:24px\">Команда MursikT7 Corporation</p>" +
               "</div></body></html>";
    }

    private static string EscapeHtml(string text) => System.Net.WebUtility.HtmlEncode(text ?? "");

    public Task SendAsync(string email, string name, string code, string purpose = "login") =>
        SendCodeAsync(email, name, code, purpose);

    public Task SendCodeAsync(string email, string name, string code, string purpose)
    {
        string subject = "Murzila-Music — код для " + PurposeTitle(purpose);
        return SendHtmlAsync(email, subject, Message(name, code, purpose), HtmlMessage(name, code, purpose));
    }

    /// <summary>Уведомление администратору о новом заказе, ожидающем подтверждения.</summary>
    public Task SendAdminAsync(string subject, string body)
    {
        string admin = Environment.GetEnvironmentVariable("MURZILA_ADMIN_EMAIL") ?? _user;
        return string.IsNullOrWhiteSpace(admin) ? Task.CompletedTask : SendTextAsync(admin, subject, body);
    }

    public Task SendTextAsync(string email, string subject, string body) =>
        SendHtmlAsync(email, subject, body, null);

    public async Task SendHtmlAsync(string email, string subject, string plain, string? html)
    {
        if (!Configured || string.IsNullOrWhiteSpace(email)) return;
        if (_port == 465)
        {
            // .NET SmtpClient умеет только STARTTLS. Порт 465 требует неявного TLS —
            // обходим это ограничение собственным минимальным SMTP-клиентом.
            await SendImplicitTlsAsync(email, subject, plain, html);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(_from),
            Subject = subject,
            Body = plain,
            IsBodyHtml = false
        };
        message.To.Add(email);
        if (!string.IsNullOrWhiteSpace(html))
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                html, new System.Net.Mime.ContentType("text/html; charset=UTF-8")));

        using var client = new SmtpClient(_host, _port)
        {
            EnableSsl = !string.Equals(Environment.GetEnvironmentVariable("SMTP_SSL"), "false",
                StringComparison.OrdinalIgnoreCase)
        };
        if (!string.IsNullOrWhiteSpace(_user)) client.Credentials = new NetworkCredential(_user, _password);
        await client.SendMailAsync(message);
    }

    /// <summary>SMTP с неявным TLS (порт 465): EHLO → AUTH LOGIN → DATA.</summary>
    private async Task SendImplicitTlsAsync(string email, string subjectText, string bodyText, string? htmlText)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port);
        using var ssl = new SslStream(tcp.GetStream(), false);
        await ssl.AuthenticateAsClientAsync(_host);
        using var reader = new StreamReader(ssl, Encoding.UTF8);
        using var writer = new StreamWriter(ssl, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

        await ReadReplyAsync(reader); // 220
        await CommandAsync(writer, reader, "EHLO " + Environment.MachineName);
        if (!string.IsNullOrWhiteSpace(_user))
        {
            await CommandAsync(writer, reader, "AUTH LOGIN");
            await CommandAsync(writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(_user)));
            await CommandAsync(writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(_password)));
        }

        string from = MailAddress.TryCreate(_from, out var parsed) ? parsed.Address : _from;
        await CommandAsync(writer, reader, $"MAIL FROM:<{from}>");
        await CommandAsync(writer, reader, $"RCPT TO:<{email}>");
        await CommandAsync(writer, reader, "DATA");

        string subject = Base64(subjectText);
        string fromHeader = MailAddress.TryCreate(_from, out parsed)
            ? Base64(parsed.DisplayName) + " <" + parsed.Address + ">"
            : "<" + from + ">";
        await writer.WriteLineAsync($"From: {fromHeader}");
        await writer.WriteLineAsync($"To: <{email}>");
        await writer.WriteLineAsync($"Subject: =?UTF-8?B?{subject}?=");
        await writer.WriteLineAsync("MIME-Version: 1.0");
        if (string.IsNullOrWhiteSpace(htmlText))
        {
            await writer.WriteLineAsync("Content-Type: text/plain; charset=UTF-8");
            await writer.WriteLineAsync("Content-Transfer-Encoding: 8bit");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(bodyText.Replace("\n", "\r\n"));
        }
        else
        {
            const string boundary = "----MurzilaMusicCode";
            await writer.WriteLineAsync($"Content-Type: multipart/alternative; boundary=\"{boundary}\"");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("--" + boundary);
            await writer.WriteLineAsync("Content-Type: text/plain; charset=UTF-8");
            await writer.WriteLineAsync("Content-Transfer-Encoding: base64");
            await writer.WriteLineAsync();
            await WriteBase64Async(writer, bodyText);
            await writer.WriteLineAsync("--" + boundary);
            await writer.WriteLineAsync("Content-Type: text/html; charset=UTF-8");
            await writer.WriteLineAsync("Content-Transfer-Encoding: base64");
            await writer.WriteLineAsync();
            await WriteBase64Async(writer, htmlText);
            await writer.WriteLineAsync("--" + boundary + "--");
        }
        await writer.WriteLineAsync(".");
        await ReadReplyAsync(reader); // 250 queued

        await CommandAsync(writer, reader, "QUIT");
    }

    private static async Task WriteBase64Async(StreamWriter writer, string text)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        for (int i = 0; i < encoded.Length; i += 76)
            await writer.WriteLineAsync(encoded[i..Math.Min(i + 76, encoded.Length)]);
    }

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static async Task<string> ReadReplyAsync(StreamReader reader)
    {
        string? line;
        string last = "";
        int code = 0;
        do
        {
            line = await reader.ReadLineAsync() ?? throw new IOException("SMTP-сервер закрыл соединение.");
            last = line;
            code = line.Length >= 3 && int.TryParse(line[..3], out int parsed) ? parsed : 0;
        } while (line.Length > 3 && line[3] == '-');

        if (code >= 400)
            throw new SmtpException((SmtpStatusCode)code, last);
        return last;
    }

    private static async Task CommandAsync(StreamWriter writer, StreamReader reader, string command)
    {
        await writer.WriteLineAsync(command);
        await ReadReplyAsync(reader);
    }
}

/// <summary>Google OAuth 2.0 через браузер, без NuGet-пакетов.</summary>
public sealed class GoogleAuth
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _clientId;
    private readonly string _secret;
    private readonly string _redirectUri;

    public GoogleAuth(string serverUrl)
    {
        _clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
        _secret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "";
        _redirectUri = Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")
                       ?? serverUrl.TrimEnd('/') + "/api/auth/google/callback";
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_secret);

    public string RedirectUri => _redirectUri;

    /// <summary>Человекочитаемая причина, почему Google-вход недоступен.</summary>
    public string Issue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_clientId) && string.IsNullOrWhiteSpace(_secret))
                return "Не заданы GOOGLE_CLIENT_ID и GOOGLE_CLIENT_SECRET.";
            if (string.IsNullOrWhiteSpace(_clientId)) return "Не задан GOOGLE_CLIENT_ID.";
            if (string.IsNullOrWhiteSpace(_secret)) return "Не задан GOOGLE_CLIENT_SECRET.";
            return LastError;
        }
    }

    public string LastError { get; private set; } = "";

    public string BuildAuthorizeUrl(string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth?client_id=" + Uri.EscapeDataString(_clientId)
        + "&redirect_uri=" + Uri.EscapeDataString(_redirectUri)
        + "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile")
        + "&state=" + Uri.EscapeDataString(state)
        + "&prompt=select_account";

    public async Task<GoogleUser?> ExchangeAsync(string code)
    {
        LastError = "";
        if (!Configured) { LastError = Issue; return null; }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _clientId,
            ["client_secret"] = _secret,
            ["redirect_uri"] = _redirectUri,
            ["grant_type"] = "authorization_code"
        });
        using var response = await Client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            string detail = "";
            try
            {
                using var error = JsonDocument.Parse(body);
                if (error.RootElement.TryGetProperty("error_description", out var description))
                    detail = description.GetString() ?? "";
                else if (error.RootElement.TryGetProperty("error", out var kind))
                    detail = kind.GetString() ?? "";
            }
            catch { /* Google вернул HTML */ }
            LastError = $"Google отклонил код ({(int)response.StatusCode}): " +
                        (detail.Length > 160 ? detail[..160] : detail) +
                        $". Redirect URI: {_redirectUri}";
            return null;
        }
        using var json = JsonDocument.Parse(body);
        string idToken = json.RootElement.TryGetProperty("id_token", out var token) ? token.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(idToken)) { LastError = "Google не вернул id_token."; return null; }

        string[] parts = idToken.Split('.');
        if (parts.Length < 2) { LastError = "Некорректный id_token."; return null; }
        string payload = Encoding.UTF8.GetString(DecodeBase64Url(parts[1]));
        using var claims = JsonDocument.Parse(payload);
        var root = claims.RootElement;

        bool audienceOk = false;
        if (root.TryGetProperty("aud", out var aud))
        {
            if (aud.ValueKind == JsonValueKind.String) audienceOk = aud.GetString() == _clientId;
            else if (aud.ValueKind == JsonValueKind.Array)
                audienceOk = aud.EnumerateArray().Any(item => item.GetString() == _clientId);
        }
        if (!audienceOk) { LastError = "id_token выдан для другого клиента Google."; return null; }

        if (root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long expires) &&
            expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        {
            LastError = "Google-токен устарел.";
            return null;
        }

        string email = root.TryGetProperty("email", out var mail) ? mail.GetString() ?? "" : "";
        string name = root.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
        string subject = root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? "" : "";
        bool verified = root.TryGetProperty("email_verified", out var verifiedClaim)
                        && verifiedClaim.ValueKind == JsonValueKind.True;
        if (string.IsNullOrWhiteSpace(email)) { LastError = "Google не вернул почту."; return null; }
        return new GoogleUser(email, name, verified, subject);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

/// <summary>SMS через sms.ru по оплате за фактические сообщения. Без ключа — тестовый режим.</summary>
public sealed class SmsRuSender
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _apiId;

    public SmsRuSender() => _apiId = Environment.GetEnvironmentVariable("SMS_RU_API_ID") ?? "";

    public bool Configured => !string.IsNullOrWhiteSpace(_apiId);

    public string Issue => Configured ? "" : "Не задан SMS_RU_API_ID — код показывается в приложении.";

    public async Task SendAsync(string phone, string text)
    {
        if (!Configured) return;
        string url = "https://sms.ru/sms/send?api_id=" + Uri.EscapeDataString(_apiId)
                     + "&to=" + Uri.EscapeDataString(phone)
                     + "&msg=" + Uri.EscapeDataString(text)
                     + "&json=1";
        string body = await Client.GetStringAsync(url);
        if (!body.Contains("\"status\":\"OK\"", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SMS.ru отклонил запрос: " +
                (body.Length > 160 ? body[..160] : body));
    }
}

/// <summary>Сокрытие личных данных в ответах и логах.</summary>
public static class Pii
{
    public static string MaskEmail(string email)
    {
        email = (email ?? "").Trim();
        int at = email.LastIndexOf('@');
        if (at <= 0) return "***";
        string local = email[..at];
        string domain = email[at..];
        string shown = local.Length switch
        {
            <= 1 => local + "***",
            <= 4 => local[0] + "***" + local[^1],
            _ => local[..2] + "****" + local[^1]
        };
        return shown + domain;
    }

    public static string MaskPhone(string phone)
    {
        string digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "***";
        string tail = digits[^4..];
        return "+* *** *** " + tail[..2] + "-" + tail[2..];
    }
}

public static class Passwords
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password, byte[] salt) => Convert.ToBase64String(
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize));

    public static bool Verify(string password, string saltBase64, string expectedBase64)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(saltBase64);
            byte[] expected = Convert.FromBase64String(expectedBase64);
            byte[] actual = Convert.FromBase64String(Hash(password, salt));
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }

    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize));
}

public sealed class Db
{
    private readonly string _connectionString;

    public Db()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(root, "MurzilaMusic");
        Directory.CreateDirectory(dir);
        string file = Environment.GetEnvironmentVariable("MURZILA_DB") ?? Path.Combine(dir, "server.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = file }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task InitializeAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Users (
                Id TEXT PRIMARY KEY,
                Email TEXT NOT NULL UNIQUE COLLATE NOCASE,
                Name TEXT NOT NULL,
                Salt TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Tariff INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Sessions (
                TokenHash TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Payments (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                Provider TEXT NOT NULL,
                ProviderPaymentId TEXT,
                Amount TEXT NOT NULL,
                Tariff INTEGER NOT NULL,
                Status TEXT NOT NULL,
                ConfirmationUrl TEXT,
                TrackId TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS PendingRegistrations (
                Email TEXT PRIMARY KEY COLLATE NOCASE,
                Name TEXT NOT NULL,
                Salt TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS LoginCodes (
                Email TEXT PRIMARY KEY COLLATE NOCASE,
                UserId TEXT NOT NULL,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS OAuthStates (
                State TEXT PRIMARY KEY,
                Token TEXT,
                UserId TEXT,
                Mode TEXT NOT NULL DEFAULT 'login',
                CreatedAt TEXT NOT NULL,
                Used INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS PasswordResetCodes (
                Email TEXT PRIMARY KEY COLLATE NOCASE,
                UserId TEXT NOT NULL,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS TwoFactorCodes (
                UserId TEXT PRIMARY KEY,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS EmailChangeCodes (
                UserId TEXT PRIMARY KEY,
                NewEmail TEXT NOT NULL COLLATE NOCASE,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS PhoneCodes (
                Phone TEXT NOT NULL,
                Purpose TEXT NOT NULL,
                UserId TEXT,
                CodeHash TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                Attempts INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (Phone, Purpose)
            );
            CREATE TABLE IF NOT EXISTS UserIdentities (
                UserId TEXT NOT NULL,
                Provider TEXT NOT NULL,
                ProviderKey TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (Provider, ProviderKey)
            );

            -- Свой магазин: заказы, доступы, промокоды, баланс, возвраты.
            CREATE TABLE IF NOT EXISTS Orders (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                ProductId TEXT NOT NULL,
                ProductKind TEXT NOT NULL,
                AmountMinor INTEGER NOT NULL,
                Currency TEXT NOT NULL DEFAULT 'RUB',
                Provider TEXT NOT NULL,
                Method TEXT NOT NULL,
                Status TEXT NOT NULL,
                ProviderRef TEXT,
                ConfirmationUrl TEXT,
                IdempotencyKey TEXT NOT NULL,
                TrackId TEXT,
                CreatedAt TEXT NOT NULL,
                PaidAt TEXT,
                UNIQUE (UserId, IdempotencyKey)
            );
            CREATE TABLE IF NOT EXISTS Entitlements (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                ProductId TEXT NOT NULL,
                ProductKind TEXT NOT NULL,
                Source TEXT NOT NULL,
                OrderId TEXT,
                Provider TEXT NOT NULL,
                ProviderRef TEXT,
                StartsAt TEXT NOT NULL,
                ExpiresAt TEXT,
                Status TEXT NOT NULL,
                RevokedAt TEXT,
                CreatedAt TEXT NOT NULL,
                UNIQUE (Provider, ProviderRef)
            );
            CREATE TABLE IF NOT EXISTS TrackUnlocks (
                UserId TEXT NOT NULL,
                TrackId TEXT NOT NULL,
                OrderId TEXT,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (UserId, TrackId)
            );
            CREATE TABLE IF NOT EXISTS PromoCodes (
                Id TEXT PRIMARY KEY,
                CodeHash TEXT NOT NULL UNIQUE,
                CodeHint TEXT NOT NULL,
                ProductId TEXT NOT NULL,
                MaxUses INTEGER NOT NULL,
                UsedCount INTEGER NOT NULL DEFAULT 0,
                ExpiresAt TEXT,
                CreatedBy TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Note TEXT
            );
            CREATE TABLE IF NOT EXISTS PromoRedemptions (
                Id TEXT PRIMARY KEY,
                CodeId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                OrderId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UNIQUE (CodeId, UserId)
            );
            CREATE TABLE IF NOT EXISTS Balances (
                UserId TEXT PRIMARY KEY,
                AmountMinor INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS BalanceLedger (
                Id TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                DeltaMinor INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                RefId TEXT,
                Description TEXT,
                CreatedAt TEXT NOT NULL,
                UNIQUE (RefId, Reason)
            );
            CREATE TABLE IF NOT EXISTS Refunds (
                Id TEXT PRIMARY KEY,
                OrderId TEXT NOT NULL UNIQUE,
                UserId TEXT NOT NULL,
                AmountMinor INTEGER NOT NULL,
                ReasonCode TEXT NOT NULL,
                Comment TEXT,
                Status TEXT NOT NULL,
                RequestedAt TEXT NOT NULL,
                DecisionNote TEXT,
                CompletedAt TEXT
            );
            CREATE TABLE IF NOT EXISTS AdminAudit (
                Id TEXT PRIMARY KEY,
                Actor TEXT NOT NULL,
                Action TEXT NOT NULL,
                Target TEXT,
                PayloadJson TEXT,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ApiApps (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ApiKeyHash TEXT NOT NULL UNIQUE,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ApiTokens (
                TokenHash TEXT PRIMARY KEY,
                AppId TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "Payments", "OrderId", "TEXT");
        await EnsureColumnAsync(connection, "OAuthStates", "Mode", "TEXT NOT NULL DEFAULT 'login'");
        await EnsureColumnAsync(connection, "Users", "Phone", "TEXT");
        await EnsureColumnAsync(connection, "Users", "PhoneVerified", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Users", "EmailVerified", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "Users", "TwoFactorEnabled", "INTEGER NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "ApiApps", "UserId", "TEXT");
        await EnsureColumnAsync(connection, "Sessions", "IsGuest", "INTEGER NOT NULL DEFAULT 0");
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string type)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        await reader.CloseAsync();
        if (columns.Contains(column)) return;
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        await alter.ExecuteNonQueryAsync();
    }

    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    public static string CodeHash(string email, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant() + ":" + code)));
    public static string PurposeCodeHash(string key, string purpose, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            key.Trim().ToLowerInvariant() + ":" + purpose + ":" + code)));

    /// <summary>Российский номер приводим к E.164; другое оставляем как есть.</summary>
    public static string NormalizePhone(string phone)
    {
        string digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && (digits[0] == '7' || digits[0] == '8')) return "+7" + digits[1..];
        if (digits.Length == 10) return "+7" + digits;
        return digits.Length > 0 ? "+" + digits : "";
    }
}

/// <summary>
/// Прямой HTTP-клиент ЮKassa (без NuGet SDK): создание платежа и проверка статуса.
/// Если ключей нет, приложение работает в локальном тестовом режиме.
/// </summary>
public sealed class YooKassaClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _shopId;
    private readonly string _secretKey;

    public YooKassaClient()
    {
        _shopId = Environment.GetEnvironmentVariable("YOOKASSA_SHOP_ID") ?? "";
        _secretKey = Environment.GetEnvironmentVariable("YOOKASSA_SECRET_KEY") ?? "";
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_shopId) && !string.IsNullOrWhiteSpace(_secretKey);
    public string Provider => Configured ? "yookassa" : "test";

    private void Authorize(HttpRequestMessage request)
    {
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_shopId}:{_secretKey}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<(string Id, string? Url)> CreateAsync(decimal amount, string description,
        string returnUrl, int tariff, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.yookassa.ru/v3/payments");
        Authorize(request);
        request.Headers.TryAddWithoutValidation("Idempotence-Key", Guid.NewGuid().ToString());
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            amount = new { value = amount.ToString("F2", CultureInfo.InvariantCulture), currency = "RUB" },
            confirmation = new { type = "redirect", return_url = returnUrl },
            capture = true,
            description,
            metadata = new { tariff = tariff.ToString(CultureInfo.InvariantCulture), user_id = userId }
        }), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(body);
        string id = json.RootElement.GetProperty("id").GetString() ?? "";
        string? url = null;
        if (json.RootElement.TryGetProperty("confirmation", out var confirmation)
            && confirmation.TryGetProperty("confirmation_url", out var value))
            url = value.GetString();
        return (id, url);
    }

    public async Task<string?> GetStatusAsync(string paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.yookassa.ru/v3/payments/" + Uri.EscapeDataString(paymentId));
        Authorize(request);
        using var response = await Client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(body);
        return json.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null;
    }
}
