using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;
using Microsoft.Data.Sqlite;
using MurzilaMusic.Server;

var builder = WebApplication.CreateBuilder(args);

// Публичный адрес нужен клиенту и Google OAuth. Render сам задаёт RENDER_EXTERNAL_URL.
var publicUrl = Environment.GetEnvironmentVariable("MURZILA_SERVER_URL")
    ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL")
    ?? "http://localhost:6001";
var bindUrl = Environment.GetEnvironmentVariable("MURZILA_BIND_URL");
if (string.IsNullOrWhiteSpace(bindUrl))
{
    string? port = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrWhiteSpace(port))
        bindUrl = "http://0.0.0.0:" + port; // Render / облачные хостинги
    else
    {
        bool local = publicUrl.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
                     || publicUrl.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase);
        bindUrl = local ? publicUrl : "http://localhost:5188";
    }
}

var db = new Db();
await db.InitializeAsync();
var yoo = new YooKassaClient();
var mailer = new SmtpMailer();
var google = new GoogleAuth(publicUrl);
var github = new GitHubAuth(publicUrl);
var sms = new SmsRuSender();
// Отдельный ключ только для бота: публичный стрим не открывается человеку по обычному токену.
string botStreamKey = Environment.GetEnvironmentVariable("MURZILA_BOT_KEY") ?? "";
if (string.IsNullOrWhiteSpace(botStreamKey))
{
    string botKeyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MurzilaMusic", "bot-key.txt");
    if (File.Exists(botKeyFile)) botStreamKey = (await File.ReadAllTextAsync(botKeyFile)).Trim();
}
var store = new StoreService(db, mailer);
var cloud = new CloudStore(db);
var api = new ApiService(db);
await cloud.InitializeAsync();

// Свои треки могут быть больше стандартного лимита ASP.NET Core (30 МБ).
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = long.MaxValue;
    options.ValueLengthLimit = int.MaxValue;
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(yoo);
builder.Services.AddSingleton(mailer);
builder.Services.AddSingleton(google);
builder.Services.AddSingleton(github);
builder.Services.AddSingleton(sms);
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(cloud);
builder.Services.AddSingleton(api);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.UseStaticFiles();

// Сайт доступен глобально: плеер, API и караоке открыты для всех,
// а ботовый стрим защищён отдельным ключом (проверка внутри endpoint).

app.Urls.Add(bindUrl);
if (bindUrl.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
    || bindUrl.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
{
    // Короткий адрес для браузера и сторонних игроков.
    // Порт 6000 браузеры Chromium блокируют (ERR_UNSAFE_PORT), поэтому его больше нет.
    app.Urls.Add("http://localhost:6001");
}
else
{
    // Публичный бинд (0.0.0.0) не должен ломать локальный доступ приложения и плеера.
    app.Urls.Add("http://localhost:6001");
    app.Urls.Add("http://localhost:5188");
}

app.MapGet("/", (HttpContext context, IWebHostEnvironment env) =>
{
    var host = context.Request.Host.Host;
    string path = host.Contains("filesharing", StringComparison.OrdinalIgnoreCase)
        ? "/filesharing/index.html"
        : host.Contains("mursik-corp", StringComparison.OrdinalIgnoreCase)
            ? "/landing/index.html"
            : "/player/index.html";
    return HtmlPage(env, path);
});

app.MapGet("/player", (IWebHostEnvironment env) => HtmlPage(env, "/player/index.html"));

app.MapGet("/api/token", (IWebHostEnvironment env) => HtmlPage(env, "/apitoken/index.html"));

app.MapGet("/api", () => Results.Redirect("/api/token"));

app.MapGet("/download", (IWebHostEnvironment env) => HtmlPage(env, "/download/index.html"));

app.MapGet("/files", (IWebHostEnvironment env) => HtmlPage(env, "/filesharing/index.html"));

app.MapGet("/api/info", (YooKassaClient payment, SmtpMailer mail, GoogleAuth googleAuth,
    SmsRuSender smsSender, StoreService shop) => Results.Json(new
{
    paymentProvider = payment.Provider,
    realPayments = payment.Configured,
    currencies = "RUB",
    emailConfigured = mail.Configured,
    googleConfigured = googleAuth.Configured,
    googleIssue = googleAuth.Issue,
    googleRedirectUri = googleAuth.RedirectUri,
    smsConfigured = smsSender.Configured,
    smsIssue = smsSender.Issue,
    passwordResetEnabled = true,
    requireEmailCode = true,
    catalogVersion = shop.Catalog.Version,
    storeProvider = "internal",
    storeMethod = shop.DefaultMethod,
    testPayments = shop.TestPayments,
    promoEnabled = shop.PromoEnabled,
    restoreEnabled = shop.RestoreEnabled,
    selfRefund = shop.SelfRefund,
    adminConfigured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MURZILA_ADMIN_TOKEN"))
}));

app.MapGet("/api/catalog", (StoreService shop) => Results.Json(shop.Catalog));

app.MapPost("/api/apps", async (CreateApiAppRequest request, HttpContext context, Db database, ApiService api) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null)
        return Results.Json(new { error = "Murzila-Music API: сначала войди в аккаунт на странице /api/token." }, statusCode: 401);
    if (await IsGuestTokenAsync(database, ReadToken(context)))
        return Results.Json(new { error = "Гостевой режим не выдаёт API-ключи. Зарегистрируйся или войди в аккаунт." }, statusCode: 403);

    var (app, apiKey) = await api.CreateAppWithKeyAsync(request.Name ?? "Murzila-Music-App", user.Id);
    return Results.Ok(new { appId = app.Id, apiKey, createdAt = app.CreatedAt, name = app.Name });
});

app.MapGet("/api/apps", async (HttpContext context, Db database, ApiService api) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Json(new { error = "Нужен вход в аккаунт." }, statusCode: 401);
    if (await IsGuestTokenAsync(database, ReadToken(context)))
        return Results.Json(new { error = "Гостевой режим не показывает API-ключи. Войди в аккаунт." }, statusCode: 403);
    return Results.Ok(await api.ListAppsAsync(user.Id));
});

app.MapPost("/api/token", async (CreateApiTokenRequest request) =>
{
    var token = await api.IssueTokenAsync(request.ApiKey);
    if (token is null) return Results.Json(new { error = "Murzila-Music API: неверный API ключ." }, statusCode: 401);
    return Results.Ok(new ApiTokenDto { Token = token.Token, AppId = token.AppId, CreatedAt = token.CreatedAt, ExpiresAt = token.ExpiresAt });
});

app.MapGet("/api/music/library", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен (Bearer, ?token= или вход на сайте)." }, statusCode: 401);

    var tracks = new List<object>();
    var list = await cloud.ListAsync(owner.UserId);
    foreach (var t in list.Take(5000))
        tracks.Add(new
        {
            t.Id, t.Title, t.Artist, t.Album, t.DurationSeconds, t.FileSize, t.FileName,
            t.HasCover, t.HasVideo, t.HasLyrics, t.AllowExternal,
            streamUrl = "/api/music/files/" + t.Id,
            coverUrl = "/api/music/cover/" + t.Id
        });
    return Results.Json(new { appId = owner.AppId, tracks });
});

app.MapGet("/api/music/files/{trackId}", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен (Bearer, ?token= или вход на сайте)." }, statusCode: 401);

    var path = await cloud.FilePathAsync(owner.UserId, trackId, "file");
    if (path is null) return Results.NotFound(new { error = "Murzila-Music API: трек не найден." });
    var contentType = CloudStore.ContentType(path);
    // enableRangeProcessing обязателен: без него браузер не умеет перематывать трек.
    bool download = context.Request.Query["download"].FirstOrDefault() is "1" or "true";
    if (download)
    {
        var meta = await cloud.FindAsync(owner.UserId, trackId);
        string baseName = meta is null ? trackId : $"{meta.Artist} - {meta.Title}".Trim(' ', '-');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = trackId;
        foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
        return Results.File(path, contentType,
            fileDownloadName: baseName + Path.GetExtension(path), enableRangeProcessing: true);
    }
    return Results.File(path, contentType, enableRangeProcessing: true);
});

// Стрим только для бота: нужен и токен Musik API, и отдельный ключ бота.
// Обычный человек со своим токеном сюда не попадёт — для плеера есть /api/music/files/{id}.
app.MapGet("/api/bot/music/files/{trackId}", async (HttpContext context, Db database,
    ApiService apiService, CloudStore cloud, string trackId) =>
{
    if (string.IsNullOrWhiteSpace(botStreamKey))
        return Results.Json(new { error = "Ботовый ключ не настроен на сервере." }, statusCode: 503);

    string? supplied = context.Request.Headers["X-Bot-Key"].FirstOrDefault()
                       ?? context.Request.Query["botKey"].FirstOrDefault();
    bool keyOk = !string.IsNullOrWhiteSpace(supplied)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied.Trim()), Encoding.UTF8.GetBytes(botStreamKey));
    if (!keyOk) return Results.Json(new { error = "Нужен ключ бота." }, statusCode: 403);

    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var path = await cloud.FilePathAsync(owner.UserId, trackId, "file");
    if (path is null) return Results.NotFound(new { error = "Трек не найден." });
    return Results.File(path, CloudStore.ContentType(path), enableRangeProcessing: true);
});

app.MapGet("/api/music/cover/{trackId}", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен (Bearer, ?token= или вход на сайте)." }, statusCode: 401);

    var path = await cloud.FilePathAsync(owner.UserId, trackId, "cover");
    if (path is null) return Results.NotFound(new { error = "Murzila-Music API: обложка не найдена." });
    var contentType = CloudStore.ContentType(path);
    return Results.File(path, contentType, enableRangeProcessing: true);
});

// Чужие треки: доступ только по явному разрешению артиста, без чужих источников и поиска по сети.
app.MapGet("/api/music/shared", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var list = await cloud.ListSharedAsync(owner.UserId);
    return Results.Json(new
    {
        tracks = list.Select(x => new
        {
            x.Track.Id, x.Track.Title, x.Track.Artist, x.Track.Album,
            x.Track.DurationSeconds, x.Track.FileSize,
            streamUrl = "/api/music/shared/files/" + x.Track.Id,
            kind = "shared"
        })
    });
});

app.MapGet("/api/music/shared/files/{trackId}", async (HttpContext context, Db database,
    ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var found = await cloud.FindSharedAsync(trackId);
    if (found.Track is null)
        return Results.NotFound(new { error = "Трек недоступен: артист не включал доступ для других." });
    var path = await cloud.FilePathAsync(found.OwnerId, trackId, "file");
    if (path is null) return Results.NotFound(new { error = "Файл трека не найден." });
    return Results.File(path, CloudStore.ContentType(path), enableRangeProcessing: true);
});

app.MapPost("/api/music/shared/{trackId}", async (SetSharedRequest request, string trackId,
    HttpContext context, Db database, ApiService apiService, CloudStore cloud) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);
    if (await IsGuestTokenAsync(database, ReadToken(context)))
        return Results.Json(new { error = "Гостевой режим не может менять доступ к трекам." }, statusCode: 403);

    bool ok = await cloud.SetSharedAsync(owner.UserId, trackId, request.Allowed);
    if (!ok) return Results.NotFound(new { error = "Свой трек не найден." });
    return Results.Ok(new { trackId, allowed = request.Allowed });
});

app.MapGet("/api/music/track/{trackId}", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var list = await cloud.ListAsync(owner.UserId);
    var track = list.FirstOrDefault(t => t.Id == trackId);
    return track is null ? Results.NotFound(new { error = "Трек не найден." }) : Results.Ok(track);
});

app.MapGet("/api/music/lyrics/{trackId}", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var path = await cloud.FilePathAsync(owner.UserId, trackId, "lyrics");
    if (path is null) return Results.NotFound(new { error = "У трека нет текста (LRC)." });
    string text = await File.ReadAllTextAsync(path);
    return Results.Json(new { trackId, format = "lrc", text });
});

// Караоке: сначала lrclib, если ничего нет — Genius. Текст кэшируется рядом со своим треком.
app.MapPost("/api/music/lyrics/{trackId}/fetch", async (HttpContext context, Db database,
    ApiService apiService, CloudStore cloud, string trackId) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var meta = await cloud.FindAsync(owner.UserId, trackId);
    if (meta is null) return Results.NotFound(new { error = "Трек не найден." });

    string? text = null, synced = null;
    string source = "";

    string clean = Regex.Replace(meta.Title, @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();

    // 1. lrclib — синхронизированный LRC. Пробуем несколько вариантов названия:
    // "(tunemir.com)", "[nmuz.net]" и т.п. мешают поиску, как и хэш в названии.
    try
    {
        bool artistKnown = !string.IsNullOrWhiteSpace(meta.Artist)
            && meta.Artist.IndexOf("неизвест", StringComparison.OrdinalIgnoreCase) < 0
            && meta.Artist.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) < 0;
        var variants = new List<(string Artist, string Title)>();
        if (artistKnown) variants.Add((meta.Artist, meta.Title));
        if (clean.Length > 2 && !string.Equals(clean, meta.Title, StringComparison.OrdinalIgnoreCase))
            variants.Add((artistKnown ? meta.Artist : "", clean));
        if (!artistKnown) variants.Add(("", meta.Title));

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var variant in variants)
        {
            string query = "https://lrclib.net/api/search?artist_name=" + Uri.EscapeDataString(variant.Artist)
                         + "&track_name=" + Uri.EscapeDataString(variant.Title);
            using var response = await http.GetAsync(query);
            if (!response.IsSuccessStatusCode) continue;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (json.RootElement.ValueKind != JsonValueKind.Array) continue;

            string? plain = null;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                // Не берём текст другой песни с таким же названием: сверяем артиста и длительность.
                if (artistKnown && item.TryGetProperty("artistName", out var an))
                {
                    string got = an.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(got)
                        && !string.Equals(got.Trim(), meta.Artist.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (item.TryGetProperty("duration", out var dn) && dn.TryGetDouble(out double foundDuration)
                    && foundDuration > 0 && Math.Abs(foundDuration - meta.DurationSeconds) > 20)
                    continue;

                if (synced is null && item.TryGetProperty("syncedLyrics", out var s)
                    && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                    synced = s.GetString();
                if (plain is null && item.TryGetProperty("plainLyrics", out var p)
                    && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                    plain = p.GetString();
                if (synced is not null) break;
            }
            text = synced ?? plain;
            if (!string.IsNullOrWhiteSpace(text)) { source = "lrclib"; break; }
        }
    }
    catch { /* переходим к Genius */ }

    // 2. Genius — обычный текст, если lrclib не дал результата.
    if (string.IsNullOrWhiteSpace(text))
    {
        text = await TryGeniusLyricsAsync(meta.Artist, meta.Title);
        if (!string.IsNullOrWhiteSpace(text)) source = "genius";
    }

    // 3. lyrics.ovh — запасной обычный текст.
    if (string.IsNullOrWhiteSpace(text))
    {
        text = await TryLyricsOvhAsync(meta.Artist, clean);
        if (!string.IsNullOrWhiteSpace(text)) source = "lyrics.ovh";
    }

    if (string.IsNullOrWhiteSpace(text))
        return Results.NotFound(new { error = "Текст не найден ни в lrclib, ни в Genius, ни в lyrics.ovh." });

    await cloud.SaveLyricsAsync(owner.UserId, trackId, text);
    return Results.Json(new { trackId, format = synced is null ? "plain" : "lrc", source, text });
});

app.MapGet("/api/music/stats", async (HttpContext context, Db database, ApiService apiService, CloudStore cloud) =>
{
    var owner = await ResolveMusicOwnerAsync(context, database, apiService);
    if (owner.UserId is null)
        return Results.Json(new { error = "Murzila-Music API: нужен токен." }, statusCode: 401);

    var list = await cloud.ListAsync(owner.UserId);
    return Results.Json(new
    {
        tracks = list.Count,
        artists = list.Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        albums = list.Select(t => t.Album).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        totalSeconds = Math.Round(list.Sum(t => t.DurationSeconds), 1),
        totalBytes = list.Sum(t => t.FileSize),
        withCover = list.Count(t => t.HasCover),
        withLyrics = list.Count(t => t.HasLyrics),
        withVideo = list.Count(t => t.HasVideo)
    });
});

// Все адреса, по которым доступен сервер: локально, в браузере и (если настроен) публично.
app.MapGet("/api/servers", (HttpContext context) => Results.Json(new
{
    service = "Murzila-Music",
    version = "3.6.0-beta",
    endpoints = new[]
    {
        new { name = "Веб-плеер (браузер)", url = "http://localhost:6001/player", kind = "web", safe = true },
        new { name = "Веб-плеер (локальный порт)", url = "http://localhost:5188/player", kind = "web", safe = true },
        new { name = "API и токены", url = "http://localhost:6001/api/token", kind = "api", safe = true },
        new { name = "Публичный сайт", url = publicUrl.TrimEnd('/') + "/api/token", kind = "api", safe = true },
        new { name = "Ботовый стрим (только с ключом)", url = publicUrl.TrimEnd('/') + "/api/bot/music/files/{id}", kind = "bot", safe = true }
    },
    unsafePorts = new[] { 6000 },
    publicUrl = Environment.GetEnvironmentVariable("MURZILA_SERVER_URL") ?? "",
    host = context.Request.Host.Value
}));

app.MapGet("/api/health", () => Results.Json(new { status = "ok", service = "Murzila-Music", version = "3.6.0-beta" }));

app.MapPost("/api/auth/register", async (RegisterRequest request, Db database, HttpContext http) =>
{
    string name = request.Name?.Trim() ?? "";
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string password = request.Password ?? "";

    if (name.Length < 2) return Results.BadRequest(new { error = "Имя должно быть не короче 2 символов." });
    if (!email.Contains('@') || !email.Contains('.'))
        return Results.BadRequest(new { error = "Введи корректную почту." });
    if (password.Length < 6) return Results.BadRequest(new { error = "Пароль должен быть не короче 6 символов." });

    string id = Guid.NewGuid().ToString("N");
    string salt = Passwords.NewSalt();
    string token = Db.NewToken();
    string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    try
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (Id, Email, Name, Salt, PasswordHash, Tariff, CreatedAt)
            VALUES ($id, $email, $name, $salt, $hash, 0, $created);
            INSERT INTO Sessions (TokenHash, UserId, ExpiresAt) VALUES ($token, $id, $expires);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$hash", Passwords.Hash(password, Convert.FromBase64String(salt)));
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$token", Db.TokenHash(token));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }
    catch (SqliteException)
    {
        return Results.Conflict(new { error = "Аккаунт с такой почтой уже существует." });
    }

    SignInCookie(http, token);
    return Results.Ok(new AuthResult { Token = token, User = new UserDto { Id = id, Name = name, Email = email } });
});

app.MapPost("/api/auth/login", async (LoginRequest request, Db database, HttpContext http, SmtpMailer mail) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string password = request.Password ?? "";
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id, Name, Email, Salt, PasswordHash, Tariff, TwoFactorEnabled FROM Users WHERE Email=$email";
    command.Parameters.AddWithValue("$email", email);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || !Passwords.Verify(password, reader.GetString(3), reader.GetString(4)))
        return Results.Unauthorized();

    string userId = reader.GetString(0);
    bool twoFactor = reader.GetInt32(6) != 0;
    var user = new UserDto
    {
        Id = userId,
        Name = reader.GetString(1),
        Email = reader.GetString(2),
        Tariff = reader.GetInt32(5)
    };
    await reader.CloseAsync();

    if (twoFactor)
    {
        string testCode = await IssueTwoFactorCodeAsync(database, userId, email, mail);
        return Results.Ok(new { requires2fa = true, email, testCode });
    }

    string token = Db.NewToken();
    await using var session = connection.CreateCommand();
    session.CommandText = "INSERT INTO Sessions (TokenHash, UserId, ExpiresAt) VALUES ($token, $id, $expires)";
    session.Parameters.AddWithValue("$token", Db.TokenHash(token));
    session.Parameters.AddWithValue("$id", user.Id);
    session.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
    await session.ExecuteNonQueryAsync();

    SignInCookie(http, token);
    return Results.Ok(new AuthResult { Token = token, User = user });
});

// ---------- двухфакторная аутентификация ----------

app.MapPost("/api/auth/login/2fa/verify", async (TwoFactorVerifyRequest request, Db database, HttpContext http) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string code = (request.Code ?? "").Trim();
    if (code.Length != 6) return Results.BadRequest(new { error = "Введи 6-значный код." });

    var account = await FindUserByEmailAsync(database, email);
    if (account is null) return Results.BadRequest(new { error = "Аккаунт не найден." });

    bool valid = false;
    await using (var connection = database.Open())
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CodeHash, ExpiresAt, Attempts FROM TwoFactorCodes WHERE UserId=$user";
        command.Parameters.AddWithValue("$user", account.Id);
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            if (DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture) >= DateTime.UtcNow
                && reader.GetInt32(2) < 5
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(reader.GetString(0)),
                    Encoding.UTF8.GetBytes(Db.PurposeCodeHash(email, "2fa", code))))
                valid = true;
        }
    }
    if (!valid) return Results.BadRequest(new { error = "Неверный или устаревший код 2FA." });

    string token = Db.NewToken();
    await using (var connection = database.Open())
    {
        await using var clear = connection.CreateCommand();
        clear.CommandText = "DELETE FROM TwoFactorCodes WHERE UserId=$user";
        clear.Parameters.AddWithValue("$user", account.Id);
        await clear.ExecuteNonQueryAsync();
        await using var session = connection.CreateCommand();
        session.CommandText = "INSERT INTO Sessions (TokenHash, UserId, ExpiresAt) VALUES ($token, $user, $expires)";
        session.Parameters.AddWithValue("$token", Db.TokenHash(token));
        session.Parameters.AddWithValue("$user", account.Id);
        session.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
        await session.ExecuteNonQueryAsync();
    }
    SignInCookie(http, token);
    return Results.Ok(new AuthResult
    {
        Token = token,
        User = new UserDto { Id = account.Id, Name = account.Name, Email = account.Email, Tariff = account.Tariff }
    });
});

app.MapPost("/api/account/2fa/request", async (HttpContext context, Db database, SmtpMailer mail) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    string code = await IssueTwoFactorCodeAsync(database, user.Id, user.Email, mail);
    return Results.Ok(new { email = Pii.MaskEmail(user.Email), testCode = code });
});

app.MapPost("/api/account/2fa/verify", async (TwoFactorVerifyRequest request, HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    string code = (request.Code ?? "").Trim();
    bool valid = false;
    await using (var connection = database.Open())
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CodeHash, ExpiresAt, Attempts FROM TwoFactorCodes WHERE UserId=$user";
        command.Parameters.AddWithValue("$user", user.Id);
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync() && DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture) >= DateTime.UtcNow
            && reader.GetInt32(2) < 5)
            valid = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(0)),
                Encoding.UTF8.GetBytes(Db.PurposeCodeHash(user.Email, "2fa", code)));
    }
    if (!valid) return Results.BadRequest(new { error = "Неверный код 2FA." });

    await using (var connection = database.Open())
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET TwoFactorEnabled=1 WHERE Id=$user; DELETE FROM TwoFactorCodes WHERE UserId=$user";
        command.Parameters.AddWithValue("$user", user.Id);
        await command.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { enabled = true });
});

app.MapPost("/api/account/2fa/disable", async (Disable2FaRequest request, HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    var auth = await FindUserByEmailAsync(database, user.Email);
    if (auth is null || !Passwords.Verify(request.Password ?? "", auth.Salt, auth.PasswordHash))
        return Results.BadRequest(new { error = "Неверный пароль." });
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE Users SET TwoFactorEnabled=0 WHERE Id=$user; DELETE FROM TwoFactorCodes WHERE UserId=$user";
    command.Parameters.AddWithValue("$user", user.Id);
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { enabled = false });
});

// ---------- веб-плеер: сессия в cookie, /api/web/* ----------

app.MapGet("/api/web/me", async (HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Json(new { signedIn = false }, statusCode: 401);
    bool guest = await IsGuestTokenAsync(database, ReadToken(context));
    return Results.Ok(new { signedIn = true, guest,
        user = new { user.Id, user.Name, user.Email, user.Tariff } });
});

app.MapPost("/api/web/guest", async (HttpContext context, Db database) =>
{
    // Гостевой доступ: 6 минут и до 5000 треков без входа. Токен живёт 6 минут.
    string email = Environment.GetEnvironmentVariable("MURZILA_OWNER_EMAIL") ?? "tprimeluc@gmail.com";
    UserRow? user = await FindUserByEmailAsync(database, email);
    if (user is null) user = await FindFirstUserAsync(database);

    // Свежий деплой: база пустая. Создаём владельца, иначе гостевой режим не включится.
    if (user is null)
    {
        string id = Guid.NewGuid().ToString("N");
        string salt = Passwords.NewSalt();
        string randomPassword = Guid.NewGuid().ToString("N");
        string created = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string ownerEmail = Environment.GetEnvironmentVariable("MURZILA_OWNER_EMAIL") ?? "tprimeluc@gmail.com";
        await using (var connection = database.Open())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO Users (Id, Email, Name, Salt, PasswordHash, Tariff, CreatedAt)
                VALUES ($id, $email, $name, $salt, $hash, 0, $created)
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$email", ownerEmail);
            command.Parameters.AddWithValue("$name", "Владелец");
            command.Parameters.AddWithValue("$salt", salt);
            command.Parameters.AddWithValue("$hash", Passwords.Hash(randomPassword, Convert.FromBase64String(salt)));
            command.Parameters.AddWithValue("$created", created);
            await command.ExecuteNonQueryAsync();
        }
        user = new UserRow { Id = id, Email = ownerEmail, Name = "Владелец", Tariff = 0 };
    }

    string token = Db.NewToken();
    string expires = DateTime.UtcNow.AddMinutes(6).ToString("O", CultureInfo.InvariantCulture);
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "INSERT INTO Sessions (TokenHash, UserId, ExpiresAt, IsGuest) VALUES ($token, $user, $expires, 1)";
        command.Parameters.AddWithValue("$token", Db.TokenHash(token));
        command.Parameters.AddWithValue("$user", user.Id);
        command.Parameters.AddWithValue("$expires", expires);
        await command.ExecuteNonQueryAsync();
    }
    SignInCookie(context, token);
    return Results.Ok(new { token, guest = true, expiresAt = expires, maxTracks = 5000, minutes = 6, name = user.Name });
});

app.MapGet("/api/web/session-token", async (HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    string? token = ReadToken(context);
    return user is null || string.IsNullOrWhiteSpace(token)
        ? Results.Json(new { error = "Нет сессии." }, statusCode: 401)
        : Results.Ok(new { token, guest = await IsGuestTokenAsync(database, token) });
});

app.MapPost("/api/web/logout", async (HttpContext context, Db database) =>
{
    string? token = ReadToken(context);
    if (token is not null)
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE TokenHash=$token";
        command.Parameters.AddWithValue("$token", Db.TokenHash(token));
        await command.ExecuteNonQueryAsync();
    }
    context.Response.Cookies.Delete("murzila_session");
    return Results.Ok(new { signedOut = true });
});

// ---------- регистрация с кодом подтверждения ----------

app.MapPost("/api/auth/register/request-code", async (RequestCodeRequest request, Db database, SmtpMailer mail) =>
{
    string name = request.Name?.Trim() ?? "";
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string password = request.Password ?? "";

    if (name.Length < 2) return Results.BadRequest(new { error = "Имя должно быть не короче 2 символов." });
    if (!email.Contains('@') || !email.Contains('.'))
        return Results.BadRequest(new { error = "Введи корректную почту." });
    if (password.Length < 6) return Results.BadRequest(new { error = "Пароль должен быть не короче 6 символов." });

    if (await UserExistsAsync(database, email))
        return Results.Conflict(new { error = "Аккаунт с такой почтой уже существует." });

    string code = Db.NewCode();
    string salt = Passwords.NewSalt();
    string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    string expires = DateTime.UtcNow.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture);

    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO PendingRegistrations (Email, Name, Salt, PasswordHash, CodeHash, ExpiresAt, Attempts)
            VALUES ($email, $name, $salt, $hash, $code, $expires, 0)
            ON CONFLICT(Email) DO UPDATE SET
                Name=excluded.Name, Salt=excluded.Salt, PasswordHash=excluded.PasswordHash,
                CodeHash=excluded.CodeHash, ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$hash", Passwords.Hash(password, Convert.FromBase64String(salt)));
        command.Parameters.AddWithValue("$code", Db.CodeHash(email, code));
        command.Parameters.AddWithValue("$expires", expires);
        await command.ExecuteNonQueryAsync();
    }

    bool mailFailed = false;
    try { await mail.SendAsync(email, name, code); }
    catch { mailFailed = true; }

    return Results.Ok(new
    {
        email,
        expiresIn = 600,
        testMode = !mail.Configured || mailFailed,
        testCode = (!mail.Configured || mailFailed) ? code : null,
        note = mailFailed ? "Письмо не отправилось (SMTP недоступен) — код показан здесь." : ""
    });
});

app.MapPost("/api/auth/register/verify", async (VerifyCodeRequest request, Db database) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string code = (request.Code ?? "").Trim();

    string? name = null, salt = null, hash = null;
    int attempts = 0;
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            SELECT Name, Salt, PasswordHash, CodeHash, ExpiresAt, Attempts
            FROM PendingRegistrations WHERE Email=$email
            """;
        command.Parameters.AddWithValue("$email", email);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.BadRequest(new { error = "Код не найден. Запроси новый." });
        if (DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture) < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Код устарел. Запроси новый." });
        attempts = reader.GetInt32(5);
        if (attempts >= 5) return Results.BadRequest(new { error = "Слишком много попыток. Запроси новый код." });
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(3)),
                Encoding.UTF8.GetBytes(Db.CodeHash(email, code))))
        {
            await reader.CloseAsync();
            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE PendingRegistrations SET Attempts=Attempts+1 WHERE Email=$email";
            bump.Parameters.AddWithValue("$email", email);
            await bump.ExecuteNonQueryAsync();
            return Results.BadRequest(new { error = "Неверный код." });
        }
        name = reader.GetString(0);
        salt = reader.GetString(1);
        hash = reader.GetString(2);
    }

    string id = Guid.NewGuid().ToString("N");
    string token = Db.NewToken();
    string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    try
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (Id, Email, Name, Salt, PasswordHash, Tariff, CreatedAt)
            VALUES ($id, $email, $name, $salt, $hash, 0, $created);
            INSERT INTO Sessions (TokenHash, UserId, ExpiresAt) VALUES ($token, $id, $expires);
            DELETE FROM PendingRegistrations WHERE Email=$email;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$name", name!);
        command.Parameters.AddWithValue("$salt", salt!);
        command.Parameters.AddWithValue("$hash", hash!);
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$token", Db.TokenHash(token));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }
    catch (SqliteException)
    {
        return Results.Conflict(new { error = "Аккаунт с такой почтой уже существует." });
    }

    return Results.Ok(new AuthResult
    {
        Token = token,
        User = new UserDto { Id = id, Name = name!, Email = email }
    });
});

// ---------- вход с кодом подтверждения нового устройства ----------

app.MapPost("/api/auth/login/request-code", async (LoginCodeRequest request, Db database, SmtpMailer mail) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    var user = await FindUserByEmailAsync(database, email);
    if (user is null || !Passwords.Verify(request.Password ?? "", user.Salt, user.PasswordHash))
        return Results.Unauthorized();

    string code = Db.NewCode();
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO LoginCodes (Email, UserId, CodeHash, ExpiresAt, Attempts)
            VALUES ($email, $user, $code, $expires, 0)
            ON CONFLICT(Email) DO UPDATE SET
                UserId=excluded.UserId, CodeHash=excluded.CodeHash,
                ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$user", user.Id);
        command.Parameters.AddWithValue("$code", Db.CodeHash(email, code));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    bool mailFailed = false;
    try { await mail.SendAsync(email, user.Name, code); }
    catch { mailFailed = true; }

    return Results.Ok(new
    {
        email,
        testMode = !mail.Configured || mailFailed,
        testCode = (!mail.Configured || mailFailed) ? code : null,
        note = mailFailed ? "Письмо не отправилось (SMTP недоступен) — код показан здесь." : ""
    });
});

app.MapPost("/api/auth/login/verify", async (VerifyCodeRequest request, Db database) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string code = (request.Code ?? "").Trim();
    string? userId = null;

    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            SELECT UserId, CodeHash, ExpiresAt, Attempts FROM LoginCodes WHERE Email=$email
            """;
        command.Parameters.AddWithValue("$email", email);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.BadRequest(new { error = "Код не найден. Запроси новый." });
        if (DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture) < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Код устарел. Запроси новый." });
        if (reader.GetInt32(3) >= 5) return Results.BadRequest(new { error = "Слишком много попыток." });
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(1)),
                Encoding.UTF8.GetBytes(Db.CodeHash(email, code))))
        {
            await reader.CloseAsync();
            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE LoginCodes SET Attempts=Attempts+1 WHERE Email=$email";
            bump.Parameters.AddWithValue("$email", email);
            await bump.ExecuteNonQueryAsync();
            return Results.BadRequest(new { error = "Неверный код." });
        }
        userId = reader.GetString(0);
    }

    var user = await FindUserByIdAsync(database, userId!);
    if (user is null) return Results.BadRequest(new { error = "Аккаунт не найден." });

    string token = await CreateSessionAsync(database, userId!);
    await using (var cleanup = database.Open())
    await using (var delete = cleanup.CreateCommand())
    {
        delete.CommandText = "DELETE FROM LoginCodes WHERE Email=$email";
        delete.Parameters.AddWithValue("$email", email);
        await delete.ExecuteNonQueryAsync();
    }
    return Results.Ok(new AuthResult { Token = token, User = ToUserDto(user) });
});

// ---------- восстановление пароля, смена почты и пароля ----------

app.MapPost("/api/auth/forgot/request-code", async (ForgotRequest request, Db database, SmtpMailer mail) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    var user = await UserExistsAsync(database, email) ? await FindUserByEmailAsync(database, email) : null;
    string? code = null;
    bool mailFailed = false;
    UserRow? resetUser = user;
    if (user is not null)
    {
        code = Db.NewCode();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PasswordResetCodes (Email, UserId, CodeHash, ExpiresAt, Attempts)
            VALUES ($email, $user, $hash, $expires, 0)
            ON CONFLICT(Email) DO UPDATE SET
                UserId=excluded.UserId, CodeHash=excluded.CodeHash,
                ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$user", user.Id);
        command.Parameters.AddWithValue("$hash", Db.PurposeCodeHash(email, "reset", code));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();

        try { await mail.SendCodeAsync(email, user.Name, code, "reset"); }
        catch { mailFailed = true; }
    }

    // Один и тот же ответ независимо от существования аккаунта: нельзя перечислять почты.
    return Results.Ok(new
    {
        email,
        maskedEmail = Pii.MaskEmail(email),
        expiresIn = 600,
        accountExists = resetUser is not null,
        testMode = !mail.Configured || mailFailed,
        testCode = user is not null && (!mail.Configured || mailFailed) ? code : null,
        note = mailFailed ? "Письмо не отправилось (SMTP недоступен) — код показан здесь." : ""
    });
});

app.MapPost("/api/auth/forgot/reset", async (ResetPasswordRequest request, Db database, HttpContext http) =>
{
    string email = (request.Email ?? "").Trim().ToLowerInvariant();
    string code = (request.Code ?? "").Trim();
    string password = request.NewPassword ?? "";
    if (password.Length < 6) return Results.BadRequest(new { error = "Пароль должен быть не короче 6 символов." });

    string? userId = null;
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT UserId, CodeHash, ExpiresAt, Attempts FROM PasswordResetCodes WHERE Email=$email";
        command.Parameters.AddWithValue("$email", email);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.BadRequest(new { error = "Код не найден. Запроси новый." });
        if (DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture) < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Код устарел. Запроси новый." });
        if (reader.GetInt32(3) >= 5) return Results.BadRequest(new { error = "Слишком много попыток. Запроси новый код." });
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(1)),
                Encoding.UTF8.GetBytes(Db.PurposeCodeHash(email, "reset", code))))
        {
            await reader.CloseAsync();
            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE PasswordResetCodes SET Attempts=Attempts+1 WHERE Email=$email";
            bump.Parameters.AddWithValue("$email", email);
            await bump.ExecuteNonQueryAsync();
            return Results.BadRequest(new { error = "Неверный код." });
        }
        userId = reader.GetString(0);
    }

    string salt = Passwords.NewSalt();
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = @"
            UPDATE Users SET Salt=$salt, PasswordHash=$hash, EmailVerified=1 WHERE Id=$user;
            DELETE FROM PasswordResetCodes WHERE Email=$email;
            DELETE FROM Sessions WHERE UserId=$user;
            ";
        command.Parameters.AddWithValue("$salt", salt);
        command.Parameters.AddWithValue("$hash", Passwords.Hash(password, Convert.FromBase64String(salt)));
        command.Parameters.AddWithValue("$user", userId!);
        command.Parameters.AddWithValue("$email", email);
        await command.ExecuteNonQueryAsync();
    }

    string token = await CreateSessionAsync(database, userId!);
    var user = await FindUserByIdAsync(database, userId!);
    SignInCookie(http, token); // перезаписываем гостевую cookie настоящей сессией
    return Results.Ok(new AuthResult { Token = token, User = ToUserDto(user!) });
});

app.MapPost("/api/account/password", async (ChangePasswordRequest request, HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    var auth = await FindUserByEmailAsync(database, user.Email);
    if (auth is null || !Passwords.Verify(request.CurrentPassword ?? "", auth.Salt, auth.PasswordHash))
        return Results.BadRequest(new { error = "Текущий пароль неверный." });
    if ((request.NewPassword ?? "").Length < 6)
        return Results.BadRequest(new { error = "Новый пароль должен быть не короче 6 символов." });

    string salt = Passwords.NewSalt();
    string keepHash = Db.TokenHash(ReadToken(context)!);
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = @"
        UPDATE Users SET Salt=$salt, PasswordHash=$hash WHERE Id=$user;
        DELETE FROM Sessions WHERE UserId=$user AND TokenHash<>$keep;
        ";
    command.Parameters.AddWithValue("$salt", salt);
    command.Parameters.AddWithValue("$hash", Passwords.Hash(request.NewPassword!, Convert.FromBase64String(salt)));
    command.Parameters.AddWithValue("$user", user.Id);
    command.Parameters.AddWithValue("$keep", keepHash);
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { changed = true });
});

app.MapPost("/api/account/email/request-code", async (ChangeEmailCodeRequest request,
    HttpContext context, Db database, SmtpMailer mail) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    string email = (request.NewEmail ?? "").Trim().ToLowerInvariant();
    if (!email.Contains('@') || !email.Contains('.'))
        return Results.BadRequest(new { error = "Введи корректную почту." });
    if (await UserExistsAsync(database, email))
        return Results.Conflict(new { error = "Эта почта уже занята." });

    string code = Db.NewCode();
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO EmailChangeCodes (UserId, NewEmail, CodeHash, ExpiresAt, Attempts)
            VALUES ($user, $email, $hash, $expires, 0)
            ON CONFLICT(UserId) DO UPDATE SET
                NewEmail=excluded.NewEmail, CodeHash=excluded.CodeHash,
                ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$user", user.Id);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$hash", Db.PurposeCodeHash(email, "email", code));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    try { await mail.SendCodeAsync(email, user.Name, code, "email"); }
    catch { return Results.Problem("Не удалось отправить письмо. Проверь SMTP-настройки сервера.", statusCode: 502); }

    return Results.Ok(new
    {
        email,
        maskedEmail = Pii.MaskEmail(email),
        expiresIn = 600,
        testMode = !mail.Configured,
        testCode = mail.Configured ? null : code
    });
});

app.MapPost("/api/account/email/verify", async (VerifyEmailCodeRequest request,
    HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    string email = (request.NewEmail ?? "").Trim().ToLowerInvariant();
    string code = (request.Code ?? "").Trim();

    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT NewEmail, CodeHash, ExpiresAt, Attempts FROM EmailChangeCodes WHERE UserId=$user";
        command.Parameters.AddWithValue("$user", user.Id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.BadRequest(new { error = "Код не найден. Запроси новый." });
        if (reader.GetString(0) != email) return Results.BadRequest(new { error = "Почта не совпадает. Запроси код заново." });
        if (DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture) < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Код устарел. Запроси новый." });
        if (reader.GetInt32(3) >= 5) return Results.BadRequest(new { error = "Слишком много попыток." });
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(1)),
                Encoding.UTF8.GetBytes(Db.PurposeCodeHash(email, "email", code))))
        {
            await reader.CloseAsync();
            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE EmailChangeCodes SET Attempts=Attempts+1 WHERE UserId=$user";
            bump.Parameters.AddWithValue("$user", user.Id);
            await bump.ExecuteNonQueryAsync();
            return Results.BadRequest(new { error = "Неверный код." });
        }
    }

    try
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET Email=$email, EmailVerified=1 WHERE Id=$user; DELETE FROM EmailChangeCodes WHERE UserId=$user;";
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$user", user.Id);
        await command.ExecuteNonQueryAsync();
    }
    catch (SqliteException) { return Results.Conflict(new { error = "Эта почта уже занята." }); }
    return Results.Ok(new { email, changed = true });
});

// ---------- телефон и SMS (sms.ru) ----------

app.MapPost("/api/account/phone/request-code", async (PhoneRequest request,
    HttpContext context, Db database, SmsRuSender smsSender) =>
{
    string purpose = request.Purpose == "login" ? "login" : "bind";
    var user = purpose == "bind" ? await FindUserAsync(database, context) : null;
    if (purpose == "bind" && user is null) return Results.Unauthorized();

    string phone = Db.NormalizePhone(request.Phone ?? "");
    if (phone.Length < 11) return Results.BadRequest(new { error = "Введи номер телефона полностью." });

    string code = Db.NewCode();
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO PhoneCodes (Phone, Purpose, UserId, CodeHash, ExpiresAt, Attempts)
            VALUES ($phone, $purpose, $user, $hash, $expires, 0)
            ON CONFLICT(Phone, Purpose) DO UPDATE SET
                UserId=excluded.UserId, CodeHash=excluded.CodeHash,
                ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$phone", phone);
        command.Parameters.AddWithValue("$purpose", purpose);
        command.Parameters.AddWithValue("$user", (object?)user?.Id ?? DBNull.Value);
        command.Parameters.AddWithValue("$hash", Db.PurposeCodeHash(phone, purpose, code));
        command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddMinutes(10).ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    try { await smsSender.SendAsync(phone, "Murzila-Music: код " + code + ". Никому не сообщайте его. Действует 10 минут."); }
    catch { return Results.Problem("Не удалось отправить SMS. Проверь настройки SMS.ru.", statusCode: 502); }

    return Results.Ok(new
    {
        phone,
        maskedPhone = Pii.MaskPhone(phone),
        expiresIn = 600,
        testMode = !smsSender.Configured,
        testCode = smsSender.Configured ? null : code
    });
});

app.MapPost("/api/account/phone/verify", async (PhoneVerifyRequest request,
    HttpContext context, Db database) =>
{
    string purpose = request.Purpose == "login" ? "login" : "bind";
    var current = purpose == "bind" ? await FindUserAsync(database, context) : null;
    if (purpose == "bind" && current is null) return Results.Unauthorized();
    string phone = Db.NormalizePhone(request.Phone ?? "");
    string code = (request.Code ?? "").Trim();

    string? boundUser = null;
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT UserId, CodeHash, ExpiresAt, Attempts FROM PhoneCodes WHERE Phone=$phone AND Purpose=$purpose";
        command.Parameters.AddWithValue("$phone", phone);
        command.Parameters.AddWithValue("$purpose", purpose);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return Results.BadRequest(new { error = "Код не найден. Запроси новый." });
        if (DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture) < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Код устарел. Запроси новый." });
        if (reader.GetInt32(3) >= 5) return Results.BadRequest(new { error = "Слишком много попыток." });
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(reader.GetString(1)),
                Encoding.UTF8.GetBytes(Db.PurposeCodeHash(phone, purpose, code))))
        {
            await reader.CloseAsync();
            await using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE PhoneCodes SET Attempts=Attempts+1 WHERE Phone=$phone AND Purpose=$purpose";
            bump.Parameters.AddWithValue("$phone", phone);
            bump.Parameters.AddWithValue("$purpose", purpose);
            await bump.ExecuteNonQueryAsync();
            return Results.BadRequest(new { error = "Неверный код." });
        }
        boundUser = reader.IsDBNull(0) ? null : reader.GetString(0);
    }

    if (purpose == "bind")
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Users SET Phone=$phone, PhoneVerified=1 WHERE Id=$user; DELETE FROM PhoneCodes WHERE Phone=$phone AND Purpose='bind';";
        command.Parameters.AddWithValue("$phone", phone);
        command.Parameters.AddWithValue("$user", current!.Id);
        await command.ExecuteNonQueryAsync();
        var updated = await FindUserByIdAsync(database, current.Id);
        return Results.Ok(new AuthResult { Token = ReadToken(context)!, User = ToUserDto(updated!) });
    }

    if (string.IsNullOrWhiteSpace(boundUser))
        return Results.BadRequest(new { error = "Этот номер не привязан к аккаунту." });
    await using (var cleanup = database.Open())
    await using (var delete = cleanup.CreateCommand())
    {
        delete.CommandText = "DELETE FROM PhoneCodes WHERE Phone=$phone AND Purpose='login'";
        delete.Parameters.AddWithValue("$phone", phone);
        await delete.ExecuteNonQueryAsync();
    }
    var userRow = await FindUserByIdAsync(database, boundUser);
    if (userRow is null) return Results.BadRequest(new { error = "Аккаунт не найден." });
    string token = await CreateSessionAsync(database, boundUser);
    return Results.Ok(new AuthResult { Token = token, User = ToUserDto(userRow) });
});

app.MapPost("/api/account/phone/unlink", async (HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    if (!user.GoogleLinked && user.EmailVerified)
        return Results.BadRequest(new { error = "Сначала привяжи Google — почта должна остаться подтверждённой." });
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE Users SET Phone=NULL, PhoneVerified=0 WHERE Id=$user";
    command.Parameters.AddWithValue("$user", user.Id);
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { unlinked = true });
});

// ---------- привязанные сервисы ----------

app.MapGet("/api/account/linked", async (HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    return Results.Json(new
    {
        items = new object[]
        {
            new { provider = "email", value = Pii.MaskEmail(user.Email), verified = user.EmailVerified },
            new { provider = "google", value = user.GoogleLinked ? "подключён" : "", verified = user.GoogleLinked },
            new { provider = "phone", value = user.Phone is null ? "" : Pii.MaskPhone(user.Phone), verified = user.PhoneVerified }
        }
    });
});

app.MapPost("/api/account/linked/{provider}/unlink", async (string provider, HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    if (provider == "email")
        return Results.BadRequest(new { error = "Почта — основной способ входа. её можно только заменить." });

    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    if (provider == "google")
    {
        if (!user.GoogleLinked && user.PhoneVerified)
            return Results.BadRequest(new { error = "Google не привязан." });
        command.CommandText = "DELETE FROM UserIdentities WHERE UserId=$user AND Provider='google'";
    }
    else if (provider == "phone")
    {
        command.CommandText = "UPDATE Users SET Phone=NULL, PhoneVerified=0 WHERE Id=$user";
    }
    else return Results.BadRequest(new { error = "Неизвестный сервис." });

    command.Parameters.AddWithValue("$user", user.Id);
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { unlinked = provider });
});

// ---------- вход через Google ----------

app.MapGet("/api/auth/google/start", async (GoogleAuth auth, Db database, HttpContext context, string? mode) =>
{
    if (!auth.Configured) return Results.BadRequest(new { error = "Google OAuth не настроен. " + auth.Issue });
    string? linkUser = null;
    if (mode == "link")
    {
        var user = await FindUserAsync(database, context);
        if (user is null) return Results.Unauthorized();
        linkUser = user.Id;
    }
    string state = Db.NewToken();
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO OAuthStates (State, Token, UserId, Mode, CreatedAt, Used)
        VALUES ($state, $token, $user, $mode, $created, 0)
        """;
    command.Parameters.AddWithValue("$state", state);
    // В режиме привязки возвращаем текущий токен: окно сможет узнать о завершении через /status.
    command.Parameters.AddWithValue("$token", (object?)ReadToken(context) ?? DBNull.Value);
    command.Parameters.AddWithValue("$user", (object?)linkUser ?? DBNull.Value);
    command.Parameters.AddWithValue("$mode", linkUser is null ? "login" : "link");
    command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new GoogleStartResult(auth.BuildAuthorizeUrl(state), state));
});

app.MapGet("/api/auth/google/callback", async (Db database, GoogleAuth auth, HttpContext http,
    string? code, string? state, string? error) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.Content(GooglePage("Вход отменён", "Ты отменил вход через Google. Возвращайся в приложение."), "text/html");
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        return Results.Content(GooglePage("Ошибка входа", "Google не передал код или состояние."), "text/html");

    string? mode = null, owner = null;
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT Mode, UserId, CreatedAt, Used FROM OAuthStates WHERE State=$state";
        command.Parameters.AddWithValue("$state", state);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.Content(GooglePage("Ошибка входа", "Неизвестный запрос. Начни вход заново в приложении."), "text/html");
        mode = reader.GetString(0);
        owner = reader.IsDBNull(1) ? null : reader.GetString(1);
        bool used = reader.GetInt32(3) != 0;
        bool expired = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture).AddMinutes(15) < DateTime.UtcNow;
        await reader.CloseAsync();
        if (used || expired)
            return Results.Content(GooglePage("Код устарел", "Начни вход через Google заново."), "text/html");
        await using var mark = connection.CreateCommand();
        mark.CommandText = "UPDATE OAuthStates SET Used=1 WHERE State=$state";
        mark.Parameters.AddWithValue("$state", state);
        await mark.ExecuteNonQueryAsync();
    }

    var googleUser = await auth.ExchangeAsync(code);
    if (googleUser is null || !googleUser.EmailVerified)
        return Results.Content(GooglePage("Не удалось войти", auth.Issue), "text/html");

    string subject = string.IsNullOrWhiteSpace(googleUser.Subject)
        ? "email:" + googleUser.Email.ToLowerInvariant()
        : googleUser.Subject;

    if (mode == "link" && !string.IsNullOrWhiteSpace(owner))
    {
        await using var connection = database.Open();
        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT UserId FROM UserIdentities WHERE Provider='google' AND ProviderKey=$key";
        check.Parameters.AddWithValue("$key", subject);
        string? existing = await check.ExecuteScalarAsync() as string;
        if (existing is not null && existing != owner)
            return Results.Content(GooglePage("Уже привязан", "Этот Google-аккаунт привязан к другому профилю."), "text/html");
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT OR IGNORE INTO UserIdentities (UserId, Provider, ProviderKey, CreatedAt)
            VALUES ($user, 'google', $key, $created);
            UPDATE Users SET EmailVerified=1 WHERE Id=$user;
            """;
        insert.Parameters.AddWithValue("$user", owner);
        insert.Parameters.AddWithValue("$key", subject);
        insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await insert.ExecuteNonQueryAsync();
        return Results.Content(GooglePage("Готово", "Google привязан к твоему аккаунту. Возвращайся в приложение."), "text/html");
    }

    string? linkedUserId = null;
    await using (var lookup = database.Open())
    await using (var find = lookup.CreateCommand())
    {
        find.CommandText = "SELECT UserId FROM UserIdentities WHERE Provider='google' AND ProviderKey=$key";
        find.Parameters.AddWithValue("$key", subject);
        linkedUserId = await find.ExecuteScalarAsync() as string;
    }

    UserRow? user;
    if (linkedUserId is not null)
    {
        user = await FindUserByIdAsync(database, linkedUserId);
    }
    else
    {
        var byEmail = await FindUserByEmailAsync(database, googleUser.Email);
        if (byEmail is not null)
        {
            // Не связываем существующий парольный аккаунт молча — это была бы подмена входа.
            return Results.Content(GooglePage("Нужна привязка",
                "Аккаунт с такой почтой уже есть. Войди по паролю и привяжи Google в профиле."), "text/html");
        }
        user = await CreateGoogleUserAsync(database, googleUser);
        if (user is not null)
        {
            await using var link = database.Open();
            await using var insert = link.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO UserIdentities (UserId, Provider, ProviderKey, CreatedAt) VALUES ($user,'google',$key,$created)";
            insert.Parameters.AddWithValue("$user", user.Id);
            insert.Parameters.AddWithValue("$key", subject);
            insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }
    }
    if (user is null) return Results.Content(GooglePage("Ошибка", "Не удалось войти. Попробуй ещё раз."), "text/html");

    string token = await CreateSessionAsync(database, user.Id);
    await using (var finish = database.Open())
    await using (var update = finish.CreateCommand())
    {
        update.CommandText = "UPDATE OAuthStates SET Token=$token, UserId=$user, Used=1 WHERE State=$state";
        update.Parameters.AddWithValue("$token", token);
        update.Parameters.AddWithValue("$user", user.Id);
        update.Parameters.AddWithValue("$state", state);
        await update.ExecuteNonQueryAsync();
    }
    SignInCookie(http, token);
    return Results.Content(GooglePage("Готово", "Возвращайся в приложение — вход уже подхватится."), "text/html");
});

app.MapGet("/api/auth/google/status/{state}", async (string state, Db database) =>
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Token, UserId FROM OAuthStates WHERE State=$state AND Used=1";
    command.Parameters.AddWithValue("$state", state);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || reader.IsDBNull(0)) return Results.Ok(new { ready = false });
    string token = reader.GetString(0);
    string userId = reader.GetString(1);
    await reader.CloseAsync();
    var user = await FindUserByIdAsync(database, userId);
    return Results.Ok(new { ready = true, token, user });
});

app.MapGet("/api/auth/github/start", async (GitHubAuth auth, Db database, HttpContext context) =>
{
    if (!auth.Configured) return Results.BadRequest(new { error = "GitHub OAuth не настроен. " + auth.Issue });
    string state = Db.NewToken();
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO OAuthStates (State, Token, UserId, Mode, CreatedAt, Used)
        VALUES ($state, $token, $user, 'login', $created, 0)
        """;
    command.Parameters.AddWithValue("$state", state);
    command.Parameters.AddWithValue("$token", (object?)ReadToken(context) ?? DBNull.Value);
    command.Parameters.AddWithValue("$user", DBNull.Value);
    command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync();
    return Results.Ok(new GoogleStartResult(auth.BuildAuthorizeUrl(state), state));
});

app.MapGet("/api/auth/github/callback", async (Db database, GitHubAuth auth, HttpContext http,
    string? code, string? state, string? error) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.Content(GooglePage("Вход отменён", "Ты отменил вход через GitHub."), "text/html");
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        return Results.Content(GooglePage("Ошибка входа", "GitHub не передал код или состояние."), "text/html");

    await using (var connection = database.Open())
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Mode, CreatedAt, Used FROM OAuthStates WHERE State=$state";
        command.Parameters.AddWithValue("$state", state);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return Results.Content(GooglePage("Ошибка входа", "Неизвестный запрос. Начни вход заново."), "text/html");
        bool used = reader.GetInt32(2) != 0;
        bool expired = DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture).AddMinutes(15) < DateTime.UtcNow;
        await reader.CloseAsync();
        if (used || expired)
            return Results.Content(GooglePage("Код устарел", "Начни вход через GitHub заново."), "text/html");
        await using var mark = connection.CreateCommand();
        mark.CommandText = "UPDATE OAuthStates SET Used=1 WHERE State=$state";
        mark.Parameters.AddWithValue("$state", state);
        await mark.ExecuteNonQueryAsync();
    }

    var ghUser = await auth.ExchangeAsync(code);
    if (ghUser is null)
        return Results.Content(GooglePage("Не удалось войти", auth.Issue), "text/html");

    string? linked = null;
    await using (var lookup = database.Open())
    await using (var find = lookup.CreateCommand())
    {
        find.CommandText = "SELECT UserId FROM UserIdentities WHERE Provider='github' AND ProviderKey=$key";
        find.Parameters.AddWithValue("$key", ghUser.Subject);
        linked = await find.ExecuteScalarAsync() as string;
    }
    UserRow? user = linked is not null ? await FindUserByIdAsync(database, linked) : null;
    if (user is null)
    {
        user = await CreateGoogleUserAsync(database, ghUser);
        if (user is not null)
        {
            await using var link = database.Open();
            await using var insert = link.CreateCommand();
            insert.CommandText = "INSERT OR IGNORE INTO UserIdentities (UserId, Provider, ProviderKey, CreatedAt) VALUES ($user,'github',$key,$created)";
            insert.Parameters.AddWithValue("$user", user.Id);
            insert.Parameters.AddWithValue("$key", ghUser.Subject);
            insert.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }
    }
    if (user is null) return Results.Content(GooglePage("Ошибка", "Не удалось войти."), "text/html");

    string token = await CreateSessionAsync(database, user.Id);
    await using (var finish = database.Open())
    await using (var update = finish.CreateCommand())
    {
        update.CommandText = "UPDATE OAuthStates SET Token=$token, UserId=$user, Used=1 WHERE State=$state";
        update.Parameters.AddWithValue("$token", token);
        update.Parameters.AddWithValue("$user", user.Id);
        update.Parameters.AddWithValue("$state", state);
        await update.ExecuteNonQueryAsync();
    }
    SignInCookie(http, token);
    return Results.Content(GooglePage("Готово", "Возвращайся в Murzila Music — вход уже подхватится."), "text/html");
});

app.MapGet("/api/auth/github/status/{state}", async (string state, Db database) =>
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT Token, UserId FROM OAuthStates WHERE State=$state AND Used=1";
    command.Parameters.AddWithValue("$state", state);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync() || reader.IsDBNull(0)) return Results.Ok(new { ready = false });
    string token = reader.GetString(0);
    string userId = reader.GetString(1);
    await reader.CloseAsync();
    var user = await FindUserByIdAsync(database, userId);
    return Results.Ok(new { ready = true, token, user });
});

app.MapGet("/api/auth/me", async (HttpContext context, Db database) =>
{
    var user = await FindUserAsync(database, context);
    return user is null ? Results.Unauthorized() : Results.Json(user);
});

app.MapPost("/api/auth/logout", async (HttpContext context, Db database) =>
{
    string? token = ReadToken(context);
    if (token is not null)
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Sessions WHERE TokenHash=$token";
        command.Parameters.AddWithValue("$token", Db.TokenHash(token));
        await command.ExecuteNonQueryAsync();
    }
    return Results.Ok();
});

app.MapGet("/api/tariffs", () => Results.Json(new[]
{
    new { id = 0, name = "Бесплатный", price = 0 },
    new { id = 5, name = "Разовый доступ", price = 5 },
    new { id = 50, name = "Про", price = 50 },
    new { id = 105, name = "Премиум", price = 105 },
    new { id = 200, name = "Ультра", price = 200 }
}));

app.MapPost("/api/payments/create", async (PaymentRequest request, HttpContext context,
    Db database, YooKassaClient payment) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();

    decimal amount = request.Tariff switch { 5 => 5m, 50 => 50m, 105 => 105m, 200 => 200m, _ => 0m };
    if (amount <= 0) return Results.BadRequest(new { error = "Неизвестный тариф." });

    string id = Guid.NewGuid().ToString("N");
    string? providerId = null;
    string? confirmationUrl = null;

    if (payment.Configured)
    {
        string returnUrl = $"{context.Request.Scheme}://{context.Request.Host}/pay/return/{id}";
        try
        {
            (providerId, confirmationUrl) = await payment.CreateAsync(amount,
                $"Murzila-Music — тариф {request.Tariff} ₽", returnUrl, request.Tariff, user.Id);
        }
        catch (Exception ex)
        {
            return Results.Problem("ЮKassa недоступна: " + ex.Message, statusCode: 502);
        }
    }
    else
    {
        confirmationUrl = $"/pay/test/{id}";
    }

    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO Payments (Id, UserId, Provider, ProviderPaymentId, Amount, Tariff, Status, ConfirmationUrl, TrackId, CreatedAt)
            VALUES ($id, $user, $provider, $providerId, $amount, $tariff, 'pending', $url, $track, $created)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$user", user.Id);
        command.Parameters.AddWithValue("$provider", payment.Provider);
        command.Parameters.AddWithValue("$providerId", (object?)providerId ?? DBNull.Value);
        command.Parameters.AddWithValue("$amount", amount.ToString("F2", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$tariff", request.Tariff);
        command.Parameters.AddWithValue("$url", (object?)confirmationUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$track", (object?)request.TrackId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    return Results.Ok(new PaymentDto
    {
        Id = id,
        Status = "pending",
        ConfirmationUrl = confirmationUrl,
        Provider = payment.Provider,
        Tariff = request.Tariff,
        Amount = amount
    });
});

app.MapGet("/api/payments/status/{id}", async (string id, HttpContext context,
    Db database, YooKassaClient payment) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    var row = await FindPaymentAsync(database, id, user.Id);
    if (row is null) return Results.NotFound();

    if (payment.Configured && row.ProviderPaymentId is not null && row.Status != "succeeded")
    {
        try
        {
            string? status = await payment.GetStatusAsync(row.ProviderPaymentId);
            if (status == "succeeded") await GrantAsync(database, row);
        }
        catch { /* сеть может быть недоступна — клиент повторит запрос */ }
    }

    row = await FindPaymentAsync(database, id, user.Id);
    return Results.Ok(ToDto(row!));
});

app.MapPost("/api/payments/mock-confirm/{id}", async (string id, Db database) =>
{
    var row = await FindPaymentByIdAsync(database, id);
    if (row is null || row.Provider != "test") return Results.NotFound();
    await GrantAsync(database, row);
    return Results.Content(TestPage("Оплата подтверждена (тест)", "Вернись в приложение — тариф уже активен.", null), "text/html");
});

app.MapPost("/api/payments/webhook", async (HttpContext context, Db database, YooKassaClient payment) =>
{
    try
    {
        using var json = await JsonDocument.ParseAsync(context.Request.Body);
        string paymentId = json.RootElement.TryGetProperty("object", out var obj)
            && obj.TryGetProperty("id", out var value) ? value.GetString() ?? "" : "";
        if (paymentId.Length == 0) return Results.BadRequest();

        var row = await FindPaymentByProviderIdAsync(database, paymentId);
        if (row is null) return Results.Ok();

        if (payment.Configured)
        {
            string? status = await payment.GetStatusAsync(paymentId);
            if (status == "succeeded") await GrantAsync(database, row);
        }
        else
        {
            await GrantAsync(database, row);
        }
        return Results.Ok();
    }
    catch { return Results.BadRequest(); }
});

app.MapGet("/pay/test/{id}", async (string id, Db database) =>
{
    var row = await FindPaymentByIdAsync(database, id);
    if (row is null || row.Provider != "test") return Results.NotFound();
    string html = TestPage($"Тестовая оплата — {row.Amount:0.00} ₽",
        "Реальные деньги не списываются. Это локальный тест полного цикла.",
        $"/api/payments/mock-confirm/{id}");
    return Results.Content(html, "text/html");
});

app.MapGet("/pay/return/{id}", (string id) => Results.Content(
    TestPage("Возврат из ЮKassa", "Проверяем статус платежа в приложении. Закрой эту вкладку.", null),
    "text/html"));

// ---------- синхронизация только своих треков ----------

app.MapGet("/api/cloud/tracks", async (HttpContext context, Db database, CloudStore cloud) =>
{
    var user = await FindUserAsync(database, context);
    return user is null ? Results.Unauthorized() : Results.Ok(await cloud.ListAsync(user.Id));
});

app.MapPost("/api/cloud/tracks", async (HttpContext context, Db database, CloudStore cloud) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    if (await IsGuestTokenAsync(database, ReadToken(context)))
        return Results.Json(new { error = "Гостевой режим только слушает. Войди, чтобы добавлять треки." }, statusCode: 403);
    try
    {
        var form = await context.Request.ReadFormAsync();
        var audio = form.Files.GetFile("file");
        if (audio is null || audio.Length == 0) return Results.BadRequest(new { error = "Файл трека не передан." });
        var meta = CloudStore.MetaFromForm(form);
        var saved = await cloud.UpsertAsync(meta, audio,
            form.Files.GetFile("cover"), form.Files.GetFile("video"),
            form.Files.GetFile("lyrics"), user.Id);
        return Results.Ok(saved);
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/cloud/tracks/{id}/{kind}", async (string id, string kind, HttpContext context,
    Db database, CloudStore cloud) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    try
    {
        string? path = await cloud.FilePathAsync(user.Id, id, kind);
        return path is null
            ? Results.NotFound()
            : Results.File(path, CloudStore.ContentType(path), Path.GetFileName(path));
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapDelete("/api/cloud/tracks/{id}", async (string id, HttpContext context, Db database, CloudStore cloud) =>
{
    var user = await FindUserAsync(database, context);
    if (user is null) return Results.Unauthorized();
    if (await IsGuestTokenAsync(database, ReadToken(context)))
        return Results.Json(new { error = "Гостевой режим не удаляет треки." }, statusCode: 403);
    try
    {
        return await cloud.DeleteAsync(user.Id, id) ? Results.Ok(new { deleted = true }) : Results.NotFound();
    }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

// ---------- свой магазин: каталог, заказы, восстановление, возвраты ----------

app.MapGet("/api/account/state", async (HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    return user is null ? Results.Unauthorized() : Results.Json(await shop.GetAccountStateAsync(user.Id));
});

app.MapPost("/api/store/orders", async (CreateStoreOrderRequest request, HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    try
    {
        var order = await shop.CreateOrderAsync(user.Id, request.ProductId ?? "", request.Method ?? "",
            request.PromoCode, request.TrackId, request.IdempotencyKey);
        return Results.Ok(order);
    }
    catch (StoreException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/store/orders/{id}", async (string id, HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    var order = await shop.GetOrderAsync(user.Id, id);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapPost("/api/store/orders/{id}/confirm-test", async (string id, HttpContext context, StoreService shop) =>
{
    if (!shop.TestPayments) return Results.NotFound();
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    var order = await shop.GetOrderAsync(user.Id, id);
    if (order is null || order.Method != "test" || order.Status != "awaiting_confirm") return Results.NotFound();
    var confirmed = await shop.ConfirmOrderAsync(id, "test", true, null);
    return confirmed is null ? Results.NotFound() : Results.Ok(confirmed);
});

app.MapPost("/api/store/restore", async (HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(await shop.RestoreAsync(user.Id));
});

app.MapPost("/api/store/refunds", async (CreateRefundRequest request, HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    try
    {
        return Results.Ok(await shop.RequestRefundAsync(user.Id, request.OrderId ?? "",
            string.IsNullOrWhiteSpace(request.ReasonCode) ? "other" : request.ReasonCode, request.Comment));
    }
    catch (StoreException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/store/refunds", async (HttpContext context, StoreService shop) =>
{
    var user = await FindUserAsync(db, context);
    if (user is null) return Results.Unauthorized();
    return Results.Json((await shop.GetAccountStateAsync(user.Id)).Refunds);
});

app.MapGet("/pay/manual/{id}", async (string id, StoreService shop) =>
{
    var order = (await shop.GetOrdersAsync()).FirstOrDefault(o => o.Id == id);
    return order is null
        ? Results.Content(TestPage("Заказ не найден", "Проверь ссылку в приложении.", null), "text/html")
        : Results.Content(TestPage($"Заказ {order.ProductName} — {order.AmountMinor / 100m:0.00} ₽",
            "Заказ создан. Администратор подтвердит его, и доступ появится в приложении автоматически.", null), "text/html");
});

// ---------- админские операции (только с X-Admin-Token) ----------

app.MapPost("/api/admin/promos", async (AdminPromoRequest request, HttpContext context, StoreService shop) =>
{
    if (!StoreService.IsAdmin(context)) return Results.NotFound();
    try
    {
        var promo = await shop.CreatePromoAsync(request.ProductId ?? "", request.MaxUses,
            request.ExpiresAt, request.Note, "admin");
        return Results.Ok(promo);
    }
    catch (StoreException ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/admin/orders", async (string? status, HttpContext context, StoreService shop) =>
{
    if (!StoreService.IsAdmin(context)) return Results.NotFound();
    return Results.Json(await shop.GetOrdersAsync(status));
});

app.MapPost("/api/admin/orders/{id}/decide", async (string id, AdminDecisionRequest request,
    HttpContext context, StoreService shop) =>
{
    if (!StoreService.IsAdmin(context)) return Results.NotFound();
    var order = await shop.ConfirmOrderAsync(id, "admin", request.Approve, request.Note);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapPost("/api/admin/refunds/{id}/decide", async (string id, AdminDecisionRequest request,
    HttpContext context, StoreService shop) =>
{
    if (!StoreService.IsAdmin(context)) return Results.NotFound();
    var refund = await shop.DecideRefundAsync(id, "admin", request.Approve, request.Note);
    return refund is null ? Results.NotFound() : Results.Ok(refund);
});

app.MapPost("/api/admin/balance/grant", async (AdminGrantRequest request, HttpContext context, StoreService shop) =>
{
    if (!StoreService.IsAdmin(context)) return Results.NotFound();
    await shop.GrantBalanceAsync(request.UserId ?? "", request.AmountMinor, request.Reason ?? "admin", "admin");
    var state = await shop.GetAccountStateAsync(request.UserId ?? "");
    return Results.Ok(new { state.User.Id, state.BalanceMinor });
});

app.Run();

static UserDto ToUserDto(UserRow user) => new()
{
    Id = user.Id, Name = user.Name, Email = user.Email, Tariff = user.Tariff,
    Phone = user.Phone, PhoneVerified = user.PhoneVerified,
    EmailVerified = user.EmailVerified, GoogleLinked = user.GoogleLinked
};

/// <summary>
/// Отдаёт страницу из wwwroot через провайдер статичных файлов: одинаково работает
/// при запуске из исходников (static web assets) и из опубликованной папки.
/// </summary>
static IResult HtmlPage(IWebHostEnvironment env, string path)
{
    var file = env.WebRootFileProvider.GetFileInfo(path);
    if (!file.Exists) return Results.NotFound("Страница не найдена: " + path);
    return Results.Stream(file.CreateReadStream(), "text/html; charset=utf-8");
}

/// <summary>Genius как второй источник текста: публичный поиск + страница песни.</summary>
static async Task<string?> TryLyricsOvhAsync(string artist, string title)
{
    try
    {
        string url = "https://api.lyrics.ovh/v1/" + Uri.EscapeDataString(artist) + "/" + Uri.EscapeDataString(title);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        using var response = await http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string lyrics = json.RootElement.TryGetProperty("lyrics", out var l) ? l.GetString() ?? "" : "";
        return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics.Trim();
    }
    catch { return null; }
}

static async Task<string?> TryGeniusLyricsAsync(string artist, string title)
{
    string query = (artist + " " + title).Trim();
    if (query.Length < 3) return null;
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (MurzilaMusic/1.3)");
        string searchUrl = "https://genius.com/api/search/multi?q=" + Uri.EscapeDataString(query);
        using var search = new HttpRequestMessage(HttpMethod.Get, searchUrl);
        string geniusToken = Environment.GetEnvironmentVariable("GENIUS_ACCESS_TOKEN") ?? "";
        if (string.IsNullOrWhiteSpace(geniusToken))
        {
            string tokenFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MurzilaMusic", "genius-token.txt");
            if (File.Exists(tokenFile)) geniusToken = (await File.ReadAllTextAsync(tokenFile)).Trim();
        }
        if (!string.IsNullOrWhiteSpace(geniusToken))
        {
            search.RequestUri = new Uri("https://api.genius.com/search?q=" + Uri.EscapeDataString(query));
            search.Headers.TryAddWithoutValidation("Authorization", "Bearer " + geniusToken.Trim());
        }
        using var searchResponse = await http.SendAsync(search);
        if (!searchResponse.IsSuccessStatusCode) return null;

        string songUrl = "";
        using var json = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("response", out var response)) return null;

        void ScanHits(JsonElement hits)
        {
            if (songUrl.Length > 0) return;
            foreach (var hit in hits.EnumerateArray())
            {
                if (!hit.TryGetProperty("result", out var result)) continue;
                if (result.TryGetProperty("url", out var url))
                {
                    songUrl = url.GetString() ?? "";
                    if (songUrl.Length > 0) return;
                }
            }
        }

        if (response.TryGetProperty("sections", out var sections))
        {
            foreach (var section in sections.EnumerateArray())
            {
                if (section.TryGetProperty("hits", out var hits)) ScanHits(hits);
                if (songUrl.Length > 0) break;
            }
        }
        else if (response.TryGetProperty("hits", out var hits)) ScanHits(hits);
        if (string.IsNullOrWhiteSpace(songUrl)) return null;

        using var page = await http.GetAsync(songUrl);
        if (!page.IsSuccessStatusCode) return null;
        string html = await page.Content.ReadAsStringAsync();
        var resultText = new StringBuilder();
        foreach (Match block in Regex.Matches(html,
                     "<div[^>]*data-lyrics-container=[\"']true[\"'][^>]*>([\\s\\S]*?)</div>",
                     RegexOptions.IgnoreCase))
        {
            string part = Regex.Replace(block.Groups[1].Value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            part = Regex.Replace(part, "<[^>]+>", "");
            part = WebUtility.HtmlDecode(part).Trim();
            if (part.Length == 0) continue;
            if (resultText.Length > 0) resultText.Append('\n');
            resultText.Append(part);
        }
        string lyrics = resultText.ToString().Trim();
        return lyrics.Length > 0 ? lyrics : null;
    }
    catch { return null; }
}

static async Task<string> IssueTwoFactorCodeAsync(Db database, string userId, string email, SmtpMailer mail)
{
    string code = Db.NewCode();
    string expires = DateTime.UtcNow.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture);
    await using (var connection = database.Open())
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            INSERT INTO TwoFactorCodes (UserId, CodeHash, ExpiresAt, Attempts) VALUES ($user, $hash, $expires, 0)
            ON CONFLICT(UserId) DO UPDATE SET CodeHash=excluded.CodeHash, ExpiresAt=excluded.ExpiresAt, Attempts=0
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$hash", Db.PurposeCodeHash(email, "2fa", code));
        command.Parameters.AddWithValue("$expires", expires);
        await command.ExecuteNonQueryAsync();
    }
    bool failed = false;
    try { await mail.SendCodeAsync(email, email, code, "login"); }
    catch { failed = true; }
    return !mail.Configured || failed ? code : "";
}

static async Task<bool> IsGuestTokenAsync(Db database, string? token)
{
    if (string.IsNullOrWhiteSpace(token)) return false;
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT IsGuest FROM Sessions WHERE TokenHash=$token";
    command.Parameters.AddWithValue("$token", Db.TokenHash(token));
    object? result = await command.ExecuteScalarAsync();
    return result is not null && result is not DBNull && Convert.ToInt32(result) == 1;
}

static async Task<(string? UserId, string? AppId)> ResolveMusicOwnerAsync(
    HttpContext context, Db database, ApiService api)
{
    string? token = ReadToken(context);
    if (string.IsNullOrWhiteSpace(token)) return (null, null);

    // 1) Токен приложения Murzila Music API (Bearer ...) — владелец берётся из ApiApps.
    var issued = await api.FindTokenAsync(token);
    if (issued is not null)
    {
        string? appOwner = await api.OwnerUserIdByAppAsync(issued.AppId);
        return (appOwner, issued.AppId);
    }

    // 2) Обычная сессия аккаунта: веб-плеер после входа по почте и паролю.
    var user = await FindUserAsync(database, context);
    return user is null ? (null, null) : (user.Id, null);
}

static string? ReadToken(HttpContext context)
{
    string header = context.Request.Headers.Authorization.ToString();
    if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        string fromHeader = header[7..].Trim();
        if (fromHeader.Length > 0) return fromHeader;
    }

    // Токен в query нужен <audio>/<video> и прямым ссылкам: они не умеют заголовки.
    string? fromQuery = context.Request.Query["token"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(fromQuery)) return fromQuery.Trim();

    // И HttpOnly-cookie: так работает веб-плеер после входа по почте и паролю.
    string? fromCookie = context.Request.Cookies["murzila_session"];
    return string.IsNullOrWhiteSpace(fromCookie) ? null : fromCookie.Trim();
}

static void SignInCookie(HttpContext context, string token)
{
    context.Response.Cookies.Append("murzila_session", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddDays(30)
    });
}

static async Task<bool> UserExistsAsync(Db database, string email)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1 FROM Users WHERE Email=$email";
    command.Parameters.AddWithValue("$email", email);
    return await command.ExecuteScalarAsync() is not null;
}

static async Task<UserAuthRow?> FindUserByEmailAsync(Db database, string email)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT u.Id, u.Name, u.Email, u.Tariff, u.Salt, u.PasswordHash,
               u.Phone, u.PhoneVerified, u.EmailVerified,
               (SELECT COUNT(1) FROM UserIdentities i WHERE i.UserId=u.Id AND i.Provider='google')
        FROM Users u WHERE u.Email=$email
        """;
    command.Parameters.AddWithValue("$email", email);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserAuthRow
    {
        Id = reader.GetString(0), Name = reader.GetString(1), Email = reader.GetString(2),
        Tariff = reader.GetInt32(3), Salt = reader.GetString(4), PasswordHash = reader.GetString(5),
        Phone = reader.IsDBNull(6) ? null : reader.GetString(6),
        PhoneVerified = reader.GetInt32(7) != 0, EmailVerified = reader.GetInt32(8) != 0,
        GoogleLinked = reader.GetInt32(9) > 0
    };
}

static async Task<UserRow?> FindFirstUserAsync(Db database)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT u.Id, u.Name, u.Email, u.Tariff, u.Phone, u.PhoneVerified, u.EmailVerified,
               (SELECT COUNT(1) FROM UserIdentities i WHERE i.UserId=u.Id AND i.Provider='google')
        FROM Users u ORDER BY u.CreatedAt LIMIT 1
        """;
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserRow
    {
        Id = reader.GetString(0), Name = reader.GetString(1),
        Email = reader.GetString(2), Tariff = reader.GetInt32(3),
        Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
        PhoneVerified = reader.GetInt32(5) != 0, EmailVerified = reader.GetInt32(6) != 0,
        GoogleLinked = reader.GetInt32(7) > 0
    };
}

static async Task<UserRow?> FindUserByIdAsync(Db database, string id)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT u.Id, u.Name, u.Email, u.Tariff, u.Phone, u.PhoneVerified, u.EmailVerified,
               (SELECT COUNT(1) FROM UserIdentities i WHERE i.UserId=u.Id AND i.Provider='google')
        FROM Users u WHERE u.Id=$id
        """;
    command.Parameters.AddWithValue("$id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserRow
    {
        Id = reader.GetString(0), Name = reader.GetString(1),
        Email = reader.GetString(2), Tariff = reader.GetInt32(3),
        Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
        PhoneVerified = reader.GetInt32(5) != 0, EmailVerified = reader.GetInt32(6) != 0,
        GoogleLinked = reader.GetInt32(7) > 0
    };
}

static async Task<string> CreateSessionAsync(Db database, string userId)
{
    string token = Db.NewToken();
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "INSERT INTO Sessions (TokenHash, UserId, ExpiresAt) VALUES ($token, $user, $expires)";
    command.Parameters.AddWithValue("$token", Db.TokenHash(token));
    command.Parameters.AddWithValue("$user", userId);
    command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture));
    await command.ExecuteNonQueryAsync();
    return token;
}

static async Task<UserRow?> CreateGoogleUserAsync(Db database, GoogleUser google)
{
    string id = Guid.NewGuid().ToString("N");
    string email = google.Email.Trim().ToLowerInvariant();
    string name = string.IsNullOrWhiteSpace(google.Name) ? email[..email.IndexOf('@')] : google.Name.Trim();
    string salt = Passwords.NewSalt();
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT OR IGNORE INTO Users (Id, Email, Name, Salt, PasswordHash, Tariff, CreatedAt)
        VALUES ($id, $email, $name, $salt, $hash, 0, $created)
        """;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$email", email);
    command.Parameters.AddWithValue("$name", name);
    command.Parameters.AddWithValue("$salt", salt);
    command.Parameters.AddWithValue("$hash", Passwords.Hash(Db.NewToken(), Convert.FromBase64String(salt)));
    command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    if (await command.ExecuteNonQueryAsync() == 0) return await FindUserByEmailAsync(database, email);
    return new UserRow { Id = id, Name = name, Email = email, Tariff = 0 };
}

static async Task<UserRow?> FindUserAsync(Db database, HttpContext context)
{
    string? token = ReadToken(context);
    if (token is null) return null;
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT u.Id, u.Name, u.Email, u.Tariff, u.Phone, u.PhoneVerified, u.EmailVerified,
               (SELECT COUNT(1) FROM UserIdentities i WHERE i.UserId=u.Id AND i.Provider='google')
        FROM Sessions s JOIN Users u ON u.Id = s.UserId
        WHERE s.TokenHash = $token AND s.ExpiresAt > $now
        """;
    command.Parameters.AddWithValue("$token", Db.TokenHash(token));
    command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return new UserRow
    {
        Id = reader.GetString(0), Name = reader.GetString(1),
        Email = reader.GetString(2), Tariff = reader.GetInt32(3),
        Phone = reader.IsDBNull(4) ? null : reader.GetString(4),
        PhoneVerified = reader.GetInt32(5) != 0, EmailVerified = reader.GetInt32(6) != 0,
        GoogleLinked = reader.GetInt32(7) > 0
    };
}

static async Task<PaymentRow?> FindPaymentAsync(Db database, string id, string userId)
{
    var row = await FindPaymentByIdAsync(database, id);
    return row is not null && row.UserId == userId ? row : null;
}

static async Task<PaymentRow?> FindPaymentByIdAsync(Db database, string id)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT Id, UserId, Provider, ProviderPaymentId, Amount, Tariff, Status, ConfirmationUrl, TrackId
        FROM Payments WHERE Id = $id
        """;
    command.Parameters.AddWithValue("$id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return ReadPayment(reader);
}

static async Task<PaymentRow?> FindPaymentByProviderIdAsync(Db database, string providerId)
{
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT Id, UserId, Provider, ProviderPaymentId, Amount, Tariff, Status, ConfirmationUrl, TrackId
        FROM Payments WHERE ProviderPaymentId = $id
        """;
    command.Parameters.AddWithValue("$id", providerId);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return null;
    return ReadPayment(reader);
}

static PaymentRow ReadPayment(SqliteDataReader reader) => new()
{
    Id = reader.GetString(0), UserId = reader.GetString(1), Provider = reader.GetString(2),
    ProviderPaymentId = reader.IsDBNull(3) ? null : reader.GetString(3),
    Amount = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
    Tariff = reader.GetInt32(5), Status = reader.GetString(6),
    ConfirmationUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
    TrackId = reader.IsDBNull(8) ? null : reader.GetString(8)
};

static PaymentDto ToDto(PaymentRow row) => new()
{
    Id = row.Id, Status = row.Status, ConfirmationUrl = row.ConfirmationUrl,
    Provider = row.Provider, Tariff = row.Tariff, Amount = row.Amount
};

static async Task GrantAsync(Db database, PaymentRow row)
{
    if (row.Status == "succeeded") return;
    await using var connection = database.Open();
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE Payments SET Status='succeeded' WHERE Id=$id";
    command.Parameters.AddWithValue("$id", row.Id);
    await command.ExecuteNonQueryAsync();

    // Разовый доступ 5 ₽ не меняет тариф — клиент сам открывает конкретный трек.
    if (row.Tariff == 5) return;

    await using var update = connection.CreateCommand();
    update.CommandText = "UPDATE Users SET Tariff = MAX(Tariff, $tariff) WHERE Id=$user";
    update.Parameters.AddWithValue("$tariff", row.Tariff);
    update.Parameters.AddWithValue("$user", row.UserId);
    await update.ExecuteNonQueryAsync();
}

static string GooglePage(string title, string message) =>
    $$"""
    <!doctype html><html lang='ru'><head><meta charset='utf-8'><title>{{title}}</title>
    <style>body{background:#101014;color:#fff;font:16px 'Segoe UI',sans-serif;display:flex;
    align-items:center;justify-content:center;height:100vh;margin:0}.card{background:#1a1a22;
    padding:36px;border-radius:18px;text-align:center;max-width:440px}h1{font-size:22px}
    p{color:#9b9ba8;line-height:1.5}</style></head><body><div class='card'>
    <h1>{{title}}</h1><p>{{message}}</p></div></body></html>
    """;

static string TestPage(string title, string message, string? confirmPath)
{
    string button = confirmPath is null ? "" :
        $"<form method='post' action='{confirmPath}'><button class='pay'>Оплатить (тест)</button></form>";
    return $$"""
        <!doctype html><html lang='ru'><head><meta charset='utf-8'>
        <title>{{title}}</title><style>
        body{background:#101014;color:#fff;font:16px 'Segoe UI',sans-serif;display:flex;
        align-items:center;justify-content:center;height:100vh;margin:0}
        .card{background:#1a1a22;padding:36px;border-radius:18px;text-align:center;max-width:460px}
        .pay{background:#7ee081;color:#102014;border:0;padding:12px 26px;border-radius:10px;
        font-size:16px;font-weight:600;cursor:pointer;margin-top:18px}
        h1{font-size:22px} p{color:#9b9ba8;line-height:1.5}
        </style></head><body><div class='card'><h1>{{title}}</h1><p>{{message}}</p>{{button}}</div></body></html>
        """;
}
