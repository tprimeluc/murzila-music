using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace MurzilaMusic.Server;

/// <summary>
/// Метаданные трека, который пользователь сам опубликовал со своего устройства.
/// Сервер хранит только файлы этого аккаунта и только по его токену.
/// </summary>
public sealed class CloudTrackMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public double DurationSeconds { get; set; }
    public string AddedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public long FileSize { get; set; }
    public string FileName { get; set; } = "";
    public bool HasCover { get; set; }
    public bool HasVideo { get; set; }
    public bool HasLyrics { get; set; }

    /// <summary>Артист сам разрешил другим слушать этот трек в Murzila Music.</summary>
    public bool AllowExternal { get; set; }
}

/// <summary>
/// Хранилище собственных треков внутри нашего сервера. Никаких внешних источников,
/// поиска или скачивания чужих файлов — только то, что загрузил владелец аккаунта.
/// </summary>
public sealed class CloudStore
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".aif" };
    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExtensions =
        { ".mp4", ".wmv", ".mkv", ".avi", ".mov", ".m4v" };

    private readonly Db _db;
    private readonly string _root;

    public CloudStore(Db db)
    {
        _db = db;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _root = Environment.GetEnvironmentVariable("MURZILA_CLOUD_DIR")
                ?? Path.Combine(appData, "MurzilaMusic", "Cloud");
        Directory.CreateDirectory(_root);
    }

    public async Task InitializeAsync()
    {
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CloudTracks (
                UserId TEXT NOT NULL,
                Id TEXT NOT NULL,
                Title TEXT NOT NULL,
                Artist TEXT NOT NULL,
                Album TEXT NOT NULL,
                DurationSeconds REAL NOT NULL DEFAULT 0,
                AddedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FileSize INTEGER NOT NULL DEFAULT 0,
                FileName TEXT NOT NULL,
                HasCover INTEGER NOT NULL DEFAULT 0,
                HasVideo INTEGER NOT NULL DEFAULT 0,
                HasLyrics INTEGER NOT NULL DEFAULT 0,
                AllowExternal INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (UserId, Id)
            );
            """;
        await command.ExecuteNonQueryAsync();

        // База уже могла быть создана старой версией — добавляем колонку без потери данных.
        try
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE CloudTracks ADD COLUMN AllowExternal INTEGER NOT NULL DEFAULT 0";
            await alter.ExecuteNonQueryAsync();
        }
        catch { /* колонка уже есть */ }
    }

    private string UserDirectory(string userId)
    {
        // userId генерируется сервером, но проверка всё равно нужна для безопасности путей.
        string safe = new string(userId.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) throw new InvalidDataException("Неверный аккаунт.");
        string dir = Path.Combine(_root, safe);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(c =>
                !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Неверный идентификатор трека.");
        return id;
    }

    private static string SafeExtension(string? name, string[] allowed)
    {
        string ext = Path.GetExtension(name ?? "").ToLowerInvariant();
        if (!allowed.Contains(ext)) throw new InvalidDataException("Неподдерживаемый формат файла.");
        return ext;
    }

    public async Task<List<CloudTrackMeta>> ListAsync(string userId)
    {
        var result = new List<CloudTrackMeta>();
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, DurationSeconds, AddedAt, UpdatedAt, FileSize, FileName,
                   HasCover, HasVideo, HasLyrics, AllowExternal
            FROM CloudTracks WHERE UserId=$user ORDER BY AddedAt DESC
            """;
        command.Parameters.AddWithValue("$user", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new CloudTrackMeta
            {
                Id = reader.GetString(0), Title = reader.GetString(1), Artist = reader.GetString(2),
                Album = reader.GetString(3), DurationSeconds = reader.GetDouble(4),
                AddedAt = reader.GetString(5), UpdatedAt = reader.GetString(6),
                FileSize = reader.GetInt64(7), FileName = reader.GetString(8),
                HasCover = reader.GetInt32(9) != 0, HasVideo = reader.GetInt32(10) != 0,
                HasLyrics = reader.GetInt32(11) != 0
            });
        }
        return result;
    }

    public async Task<CloudTrackMeta?> FindAsync(string userId, string id)
    {
        string safeId = SafeId(id);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Title, Artist, Album, DurationSeconds, AddedAt, UpdatedAt, FileSize, FileName,
                   HasCover, HasVideo, HasLyrics, AllowExternal
            FROM CloudTracks WHERE UserId=$user AND Id=$id
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", safeId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new CloudTrackMeta
        {
            Id = reader.GetString(0), Title = reader.GetString(1), Artist = reader.GetString(2),
            Album = reader.GetString(3), DurationSeconds = reader.GetDouble(4),
            AddedAt = reader.GetString(5), UpdatedAt = reader.GetString(6),
            FileSize = reader.GetInt64(7), FileName = reader.GetString(8),
            HasCover = reader.GetInt32(9) != 0, HasVideo = reader.GetInt32(10) != 0,
            HasLyrics = reader.GetInt32(11) != 0, AllowExternal = reader.GetInt32(12) != 0
        };
    }

    /// <summary>Треки других аккаунтов, которые сами артисты разрешили слушать.</summary>
    public async Task<List<(string OwnerId, CloudTrackMeta Track)>> ListSharedAsync(string exceptUserId)
    {
        var result = new List<(string, CloudTrackMeta)>();
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserId, Id, Title, Artist, Album, DurationSeconds, AddedAt, UpdatedAt, FileSize, FileName,
                   HasCover, HasVideo, HasLyrics
            FROM CloudTracks WHERE AllowExternal=1 AND UserId<>$user
            ORDER BY AddedAt DESC LIMIT 5000
            """;
        command.Parameters.AddWithValue("$user", exceptUserId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), new CloudTrackMeta
            {
                Id = reader.GetString(1), Title = reader.GetString(2), Artist = reader.GetString(3),
                Album = reader.GetString(4), DurationSeconds = reader.GetDouble(5),
                AddedAt = reader.GetString(6), UpdatedAt = reader.GetString(7),
                FileSize = reader.GetInt64(8), FileName = reader.GetString(9),
                HasCover = reader.GetInt32(10) != 0, HasVideo = reader.GetInt32(11) != 0,
                HasLyrics = reader.GetInt32(12) != 0, AllowExternal = true
            }));
        return result;
    }

    /// <summary>Находит чужой трек, только если артист явно включил доступ.</summary>
    public async Task<(string OwnerId, CloudTrackMeta? Track)> FindSharedAsync(string id)
    {
        string safeId = SafeId(id);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserId, Id, Title, Artist, Album, DurationSeconds, AddedAt, UpdatedAt, FileSize, FileName,
                   HasCover, HasVideo, HasLyrics
            FROM CloudTracks WHERE Id=$id AND AllowExternal=1 LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", safeId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return ("", null);
        return (reader.GetString(0), new CloudTrackMeta
        {
            Id = reader.GetString(1), Title = reader.GetString(2), Artist = reader.GetString(3),
            Album = reader.GetString(4), DurationSeconds = reader.GetDouble(5),
            AddedAt = reader.GetString(6), UpdatedAt = reader.GetString(7),
            FileSize = reader.GetInt64(8), FileName = reader.GetString(9),
            HasCover = reader.GetInt32(10) != 0, HasVideo = reader.GetInt32(11) != 0,
            HasLyrics = reader.GetInt32(12) != 0, AllowExternal = true
        });
    }

    /// <summary>Артист включает или выключает доступ к своему треку для других слушателей.</summary>
    public async Task<bool> SetSharedAsync(string userId, string id, bool allowed)
    {
        string safeId = SafeId(id);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudTracks SET AllowExternal=$flag WHERE UserId=$user AND Id=$id";
        command.Parameters.AddWithValue("$flag", allowed ? 1 : 0);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", safeId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<CloudTrackMeta> UpsertAsync(CloudTrackMeta meta, IFormFile audio,
        IFormFile? cover, IFormFile? video, IFormFile? lyrics, string userId)
    {
        meta.Id = SafeId(meta.Id);
        string dir = UserDirectory(userId);
        string ext = SafeExtension(audio.FileName, AudioExtensions);
        await SaveAsync(audio, Path.Combine(dir, meta.Id + ext));

        bool hasCover = false, hasVideo = false, hasLyrics = false;
        if (cover is not null && cover.Length > 0)
        {
            await SaveAsync(cover, Path.Combine(dir, meta.Id + ".cover" + SafeExtension(cover.FileName, CoverExtensions)));
            hasCover = true;
        }
        if (video is not null && video.Length > 0)
        {
            await SaveAsync(video, Path.Combine(dir, meta.Id + ".video" + SafeExtension(video.FileName, VideoExtensions)));
            hasVideo = true;
        }
        if (lyrics is not null && lyrics.Length > 0)
        {
            await SaveAsync(lyrics, Path.Combine(dir, meta.Id + ".lrc"));
            hasLyrics = true;
        }

        meta.FileName = meta.Id + ext;
        meta.FileSize = audio.Length;
        meta.HasCover = hasCover;
        meta.HasVideo = hasVideo;
        meta.HasLyrics = hasLyrics;
        meta.UpdatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudTracks (UserId, Id, Title, Artist, Album, DurationSeconds, AddedAt,
                                     UpdatedAt, FileSize, FileName, HasCover, HasVideo, HasLyrics, AllowExternal)
            VALUES ($user, $id, $title, $artist, $album, $duration, $added,
                    $updated, $size, $file, $cover, $video, $lyrics, $shared)
            ON CONFLICT(UserId, Id) DO UPDATE SET
                Title=excluded.Title, Artist=excluded.Artist, Album=excluded.Album,
                DurationSeconds=excluded.DurationSeconds, AddedAt=excluded.AddedAt,
                UpdatedAt=excluded.UpdatedAt, FileSize=excluded.FileSize, FileName=excluded.FileName,
                HasCover=excluded.HasCover, HasVideo=excluded.HasVideo, HasLyrics=excluded.HasLyrics,
                AllowExternal=excluded.AllowExternal
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", meta.Id);
        command.Parameters.AddWithValue("$title", meta.Title);
        command.Parameters.AddWithValue("$artist", meta.Artist);
        command.Parameters.AddWithValue("$album", meta.Album);
        command.Parameters.AddWithValue("$duration", meta.DurationSeconds);
        command.Parameters.AddWithValue("$added", string.IsNullOrWhiteSpace(meta.AddedAt)
            ? meta.UpdatedAt : meta.AddedAt);
        command.Parameters.AddWithValue("$updated", meta.UpdatedAt);
        command.Parameters.AddWithValue("$size", meta.FileSize);
        command.Parameters.AddWithValue("$file", meta.FileName);
        command.Parameters.AddWithValue("$cover", hasCover ? 1 : 0);
        command.Parameters.AddWithValue("$video", hasVideo ? 1 : 0);
        command.Parameters.AddWithValue("$lyrics", hasLyrics ? 1 : 0);
        command.Parameters.AddWithValue("$shared", meta.AllowExternal ? 1 : 0);
        await command.ExecuteNonQueryAsync();
        return meta;
    }

    public async Task<string?> FilePathAsync(string userId, string id, string kind)
    {
        string safeId = SafeId(id);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FileName, HasCover, HasVideo, HasLyrics FROM CloudTracks
            WHERE UserId=$user AND Id=$id
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", safeId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        string file = reader.GetString(0);
        bool cover = reader.GetInt32(1) != 0, video = reader.GetInt32(2) != 0, lyrics = reader.GetInt32(3) != 0;
        string? name = kind switch
        {
            "file" => file,
            "cover" when cover => Directory.GetFiles(UserDirectory(userId), safeId + ".cover.*").FirstOrDefault(),
            "video" when video => Directory.GetFiles(UserDirectory(userId), safeId + ".video.*").FirstOrDefault(),
            "lyrics" when lyrics => Path.Combine(UserDirectory(userId), safeId + ".lrc"),
            _ => null
        };
        if (name is null) return null;
        string path = Path.Combine(UserDirectory(userId), name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Сохраняет найденный текст (LRC) рядом с треком и включает HasLyrics.</summary>
    public async Task<bool> SaveLyricsAsync(string userId, string id, string text)
    {
        string safeId = SafeId(id);
        string dir = UserDirectory(userId);
        string path = Path.Combine(dir, safeId + ".lrc");
        await File.WriteAllTextAsync(path, text);
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudTracks SET HasLyrics=1 WHERE UserId=$user AND Id=$id";
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", safeId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        string safeId = SafeId(id);
        string dir = UserDirectory(userId);
        foreach (string file in Directory.GetFiles(dir).Where(f =>
                     Path.GetFileName(f).StartsWith(safeId + ".", StringComparison.Ordinal)))
        {
            try { File.Delete(file); } catch { }
        }
        await using var connection = _db.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudTracks WHERE UserId=$user AND Id=$id";
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$id", safeId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private static async Task SaveAsync(IFormFile source, string target)
    {
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = source.OpenReadStream())
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
                await input.CopyToAsync(output);
            File.Move(temp, target, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static CloudTrackMeta MetaFromForm(IFormCollection form)
    {
        double.TryParse(form["durationSeconds"], NumberStyles.Any, CultureInfo.InvariantCulture, out double duration);
        long.TryParse(form["fileSize"], NumberStyles.Any, CultureInfo.InvariantCulture, out long size);
        bool Bool(string key) => string.Equals(form[key], "true", StringComparison.OrdinalIgnoreCase)
                                 || form[key] == "1";
        return new CloudTrackMeta
        {
            Id = form["id"].ToString(),
            Title = form["title"].ToString(),
            Artist = form["artist"].ToString(),
            Album = form["album"].ToString(),
            DurationSeconds = duration,
            AddedAt = form["addedAt"].ToString(),
            FileSize = size,
            HasCover = Bool("hasCover"),
            HasVideo = Bool("hasVideo"),
            HasLyrics = Bool("hasLyrics"),
            AllowExternal = Bool("allowExternal")
        };
    }

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".m4a" or ".aac" => "audio/mp4",
        ".ogg" => "audio/ogg",
        ".flac" => "audio/flac",
        ".wma" => "audio/x-ms-wma",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".lrc" => "text/plain; charset=utf-8",
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".mov" => "video/quicktime",
        _ => "application/octet-stream"
    };
}
