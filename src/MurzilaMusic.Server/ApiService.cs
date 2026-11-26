using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MurzilaMusic.Server;

public sealed class ApiAppDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class ApiTokenDto
{
    public string Token { get; set; } = "";
    public string AppId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

/// <summary>
/// Лёгкий Murzila Music API: создание приложений, выдача API-токенов
/// и чтение треков собственной библиотеки владельца.
/// </summary>
public sealed class ApiService
{
    private readonly Db _db;

    public ApiService(Db db)
    {
        _db = db;
    }

    public async Task<(ApiAppDto App, string ApiKey)> CreateAppWithKeyAsync(string name, string? userId)
    {
        string id = Guid.NewGuid().ToString("N");
        string apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        string created = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ApiApps (Id, Name, ApiKeyHash, UserId, CreatedAt)
            VALUES ($id, $name, $hash, $user, $created);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? "Murzila-Music-App" : name.Trim());
        command.Parameters.AddWithValue("$hash", ApiKeyHash(apiKey));
        command.Parameters.AddWithValue("$user", (object?)userId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", created);
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException)
        {
            throw new InvalidOperationException("Murzila-Music API: приложение с таким именем уже зарегистрировано.");
        }
        return (new ApiAppDto { Id = id, Name = name?.Trim() ?? "Murzila-Music-App", CreatedAt = created }, apiKey);
    }

    public static string ApiKeyHash(string apiKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Trim().ToLowerInvariant())));

    public async Task<ApiTokenDto?> IssueTokenAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        string hash = ApiKeyHash(apiKey);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM ApiApps WHERE ApiKeyHash=$hash";
        command.Parameters.AddWithValue("$hash", hash);
        object? result = await command.ExecuteScalarAsync();
        if (result is null) return null;
        string appId = Convert.ToString(result)!;

        string token = Db.NewToken();
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        string expires = DateTime.UtcNow.AddDays(30).ToString("O", CultureInfo.InvariantCulture);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO ApiTokens (TokenHash, AppId, CreatedAt, ExpiresAt)
                VALUES ($token, $app, $created, $expires);
                """;
            insert.Parameters.AddWithValue("$token", Db.TokenHash(token));
            insert.Parameters.AddWithValue("$app", appId);
            insert.Parameters.AddWithValue("$created", now);
            insert.Parameters.AddWithValue("$expires", expires);
            try { await insert.ExecuteNonQueryAsync(); }
            catch (SqliteException) { }
        }
        return new ApiTokenDto { Token = token, AppId = appId, CreatedAt = now, ExpiresAt = expires };
    }

    public async Task<ApiTokenDto?> FindTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        string hash = Db.TokenHash(token);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AppId, CreatedAt, ExpiresAt FROM ApiTokens WHERE TokenHash=$token";
        command.Parameters.AddWithValue("$token", hash);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new ApiTokenDto
        {
            Token = token,
            AppId = reader.GetString(0),
            CreatedAt = reader.GetString(1),
            ExpiresAt = reader.GetString(2)
        };
    }

    /// <summary>Владелец приложения: треки отдаются только из его собственной библиотеки.</summary>
    public async Task<string?> OwnerUserIdByAppAsync(string appId)
    {
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId FROM ApiApps WHERE Id=$id";
        command.Parameters.AddWithValue("$id", appId);
        object? result = await command.ExecuteScalarAsync();
        if (result is null || result is DBNull) return null;
        return Convert.ToString(result);
    }

    public async Task<bool> DeleteAppAsync(string userId, string appId)
    {
        await using var connection = _db.Open();
        await using var owner = connection.CreateCommand();
        owner.CommandText = "SELECT Id FROM ApiApps WHERE Id=$id AND UserId=$user";
        owner.Parameters.AddWithValue("$id", appId);
        owner.Parameters.AddWithValue("$user", userId);
        if (await owner.ExecuteScalarAsync() is null) return false;
        await using var delTokens = connection.CreateCommand();
        delTokens.CommandText = "DELETE FROM ApiTokens WHERE AppId=$id";
        delTokens.Parameters.AddWithValue("$id", appId);
        await delTokens.ExecuteNonQueryAsync();
        await using var delApp = connection.CreateCommand();
        delApp.CommandText = "DELETE FROM ApiApps WHERE Id=$id";
        delApp.Parameters.AddWithValue("$id", appId);
        await delApp.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<(string ApiKey, ApiTokenDto Token)?> ResetAppAsync(string userId, string appId)
    {
        await using var connection = _db.Open();
        await using var owner = connection.CreateCommand();
        owner.CommandText = "SELECT Id FROM ApiApps WHERE Id=$id AND UserId=$user";
        owner.Parameters.AddWithValue("$id", appId);
        owner.Parameters.AddWithValue("$user", userId);
        if (await owner.ExecuteScalarAsync() is null) return null;
        string apiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await using var upd = connection.CreateCommand();
        upd.CommandText = "UPDATE ApiApps SET ApiKeyHash=$hash WHERE Id=$id";
        upd.Parameters.AddWithValue("$hash", ApiKeyHash(apiKey));
        upd.Parameters.AddWithValue("$id", appId);
        await upd.ExecuteNonQueryAsync();
        await using var del = connection.CreateCommand();
        del.CommandText = "DELETE FROM ApiTokens WHERE AppId=$id";
        del.Parameters.AddWithValue("$id", appId);
        await del.ExecuteNonQueryAsync();
        var token = await IssueTokenAsync(apiKey);
        return token is null ? null : (apiKey, token);
    }

    /// <summary>Приложения, созданные конкретным аккаунтом (для страницы /api/token).</summary>
    public async Task<List<ApiAppDto>> ListAppsAsync(string userId)
    {
        var list = new List<ApiAppDto>();
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, CreatedAt FROM ApiApps WHERE UserId=$user ORDER BY CreatedAt DESC";
        command.Parameters.AddWithValue("$user", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(new ApiAppDto { Id = reader.GetString(0), Name = reader.GetString(1), CreatedAt = reader.GetString(2) });
        return list;
    }
}
