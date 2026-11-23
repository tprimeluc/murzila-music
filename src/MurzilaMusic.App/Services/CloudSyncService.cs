using System.Globalization;
using System.Text;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

public sealed record SyncSummary(int Uploaded, int Downloaded, int Skipped, string Message);

/// <summary>
/// Синхронизирует только те треки, которые пользователь сам отметил для публикации.
/// На другом устройстве достаточно войти в тот же аккаунт — чужие треки не ищутся и не скачиваются.
/// </summary>
public sealed class CloudSyncService
{
    private readonly LibraryService _library;

    public CloudSyncService(LibraryService library) => _library = library;

    public async Task<SyncSummary> RunAsync(IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(SettingsService.Current.AccessToken))
            throw new InvalidOperationException("Сначала войди в аккаунт — синхронизация работает только со своим сервером.");

        var errors = new List<string>();
        int uploaded = 0, downloaded = 0, skipped = 0;

        progress?.Report("Список твоих треков на сервере…");
        var remote = await CloudApiClient.ListAsync();
        var remoteById = remote.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        // 1. Отправляем на сервер все свои локальные файлы. Отдельная кнопка
        // «Опубликовать» не нужна: добавил трек — он сразу доступен в веб-плеере.
        foreach (var track in _library.Tracks.ToList())
        {
            if (!File.Exists(track.FileName)) continue;

            // Текст, который приложение уже нашло и закэшировало, тоже уезжает на сервер.
            string? lyricsPath = File.Exists(track.LyricsFile ?? "") ? track.LyricsFile : null;
            string? tempLrc = null;
            if (lyricsPath is null || lyricsPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string cached = Path.Combine(AppPaths.Lyrics, track.Id + ".json");
                if (File.Exists(cached))
                {
                    tempLrc = ToLrc(cached);
                    if (tempLrc is not null) lyricsPath = tempLrc;
                }
            }
            bool hasLyrics = !string.IsNullOrWhiteSpace(lyricsPath);

            if (remoteById.TryGetValue(track.Id, out var existing)
                && existing.FileSize == new FileInfo(track.FileName).Length
                && existing.Title == track.Title && existing.Artist == track.Artist
                && existing.Album == track.Album
                && existing.AllowExternal == track.AllowExternal
                && existing.HasLyrics == hasLyrics
                && Math.Abs(existing.DurationSeconds - track.DurationSeconds) < 0.5)
            {
                skipped++;
                continue;
            }

            progress?.Report($"Загрузка «{track.Title}»…");
            var report = new Progress<CloudProgress>(p =>
                progress?.Report($"Загрузка «{track.Title}»: {p.Percent}%"));
            var meta = new CloudTrackDto
            {
                Id = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                DurationSeconds = track.DurationSeconds,
                AllowExternal = track.AllowExternal,
                AddedAt = track.AddedAt.ToString("O", CultureInfo.InvariantCulture)
            };
            try
            {
                await CloudApiClient.UploadAsync(meta, track.FileName,
                    File.Exists(track.CoverFile ?? "") ? track.CoverFile : null,
                    File.Exists(track.VideoFile ?? "") ? track.VideoFile : null,
                    lyricsPath, report);
                track.IsPublished = true;
                uploaded++;
            }
            catch (Exception ex)
            {
                errors.Add($"{track.Title}: {ex.Message}");
            }
            finally
            {
                if (tempLrc is not null)
                {
                    try { File.Delete(tempLrc); } catch { }
                }
            }
        }

        // 2. Скачиваем только то, что лежит в этом же аккаунте.
        if (uploaded > 0) remote = await CloudApiClient.ListAsync();
        foreach (var item in remote)
        {
            string target = Path.Combine(AppPaths.Tracks, Path.GetFileName(item.FileName));
            var local = _library.Find(item.Id);
            if (local is not null && File.Exists(local.FileName)
                && new FileInfo(local.FileName).Length == item.FileSize)
            {
                skipped++;
                continue;
            }

            progress?.Report($"Скачивание «{item.Title}»…");
            var report = new Progress<CloudProgress>(p =>
                progress?.Report($"Скачивание «{item.Title}»: {p.Percent}%"));
            try
            {
                await CloudApiClient.DownloadAsync(item.Id, "file", target, report);

                string? cover = null, video = null, lyrics = null;
                if (item.HasCover)
                {
                    cover = Path.Combine(AppPaths.Covers, item.Id + ".cover.jpg");
                    await CloudApiClient.DownloadAsync(item.Id, "cover", cover);
                }
                if (item.HasVideo)
                {
                    video = Path.Combine(AppPaths.Videos, item.Id + ".video.mp4");
                    await CloudApiClient.DownloadAsync(item.Id, "video", video);
                }
                if (item.HasLyrics)
                {
                    lyrics = Path.Combine(AppPaths.Lyrics, item.Id + ".lrc");
                    await CloudApiClient.DownloadAsync(item.Id, "lyrics", lyrics);
                }

                var track = local ?? new TrackItem { Id = item.Id };
                track.Title = item.Title;
                track.Artist = item.Artist;
                track.Album = item.Album;
                track.DurationSeconds = item.DurationSeconds;
                track.FileName = target;
                track.CoverFile = cover;
                track.VideoFile = video;
                track.LyricsFile = lyrics;
                track.IsPublished = true;
                if (track.AddedAt == default && DateTimeOffset.TryParse(item.AddedAt,
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var added))
                    track.AddedAt = added.LocalDateTime;
                _library.AddOrReplace(track);
                downloaded++;
            }
            catch (Exception ex)
            {
                errors.Add($"{item.Title}: {ex.Message}");
            }
        }

        _library.Save();
        string message = $"Синхронизировано: загружено {uploaded}, скачано {downloaded}, без изменений {skipped}.";
        if (errors.Count > 0) message += " Ошибки: " + string.Join("; ", errors.Take(3));
        return new SyncSummary(uploaded, downloaded, skipped, message);
    }

    /// <summary>Кэш приложения — JSON. На сервер и в веб-караоке нужен обычный LRC.</summary>
    private static string? ToLrc(string cacheJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(cacheJson));
            if (!doc.RootElement.TryGetProperty("Lines", out var lines)
                || lines.ValueKind != System.Text.Json.JsonValueKind.Array) return null;

            var text = new StringBuilder();
            foreach (var line in lines.EnumerateArray())
            {
                string raw = line.TryGetProperty("RawText", out var r) ? r.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(raw)) continue;
                double start = line.TryGetProperty("Start", out var s) && s.TryGetDouble(out var d) ? d : 0;
                if (start > 0)
                {
                    var ts = TimeSpan.FromSeconds(start);
                    text.Append('[').Append((int)ts.TotalMinutes).Append(':')
                        .Append(ts.Seconds.ToString("00")).Append('.')
                        .Append(ts.Milliseconds.ToString("000")).Append(']');
                }
                text.AppendLine(raw);
            }
            if (text.Length == 0) return null;

            string path = Path.Combine(Path.GetTempPath(), "murzila-lyrics-" + Guid.NewGuid().ToString("N") + ".lrc");
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch { return null; }
    }
}
