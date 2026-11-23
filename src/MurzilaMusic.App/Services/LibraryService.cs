using System.Text.Json;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Библиотека пользователя. Важно: приложение НИКОГДА не скачивает и не ищет треки
/// во внешних источниках — только файлы, выбранные пользователем на своём устройстве.
/// </summary>
public sealed class LibraryService
{
    private static readonly string[] AudioExtensions =
        { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".aiff", ".aif" };

    private static readonly string[] VideoExtensions =
        { ".mp4", ".wmv", ".mkv", ".avi", ".mov", ".m4v" };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LibraryData Data { get; private set; } = new();

    public LibraryService()
    {
        AppPaths.Ensure();
        Load();
    }

    public IReadOnlyList<TrackItem> Tracks => Data.Tracks;
    public IReadOnlyList<Playlist> Playlists => Data.Playlists;

    private void Load()
    {
        try
        {
            if (File.Exists(AppPaths.LibraryFile))
            {
                var data = JsonSerializer.Deserialize<LibraryData>(File.ReadAllText(AppPaths.LibraryFile), JsonOptions);
                if (data is not null)
                {
                    Data = data;
                    foreach (var t in Data.Tracks)
                    {
                        if (!string.IsNullOrEmpty(t.FileName) && !File.Exists(t.FileName))
                            t.FileName = Path.Combine(AppPaths.Tracks, Path.GetFileName(t.FileName));
                    }
                }
            }
        }
        catch
        {
            Data = new LibraryData();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.LibraryFile, JsonSerializer.Serialize(Data, JsonOptions));
        }
        catch
        {
            // см. политику конфиденциальности: никаких сетевых отправок библиотеки нет
        }
    }

    /// <summary>Добавить трек из личного облака или обновить его метаданные.</summary>
    public void AddOrReplace(TrackItem track)
    {
        int index = Data.Tracks.FindIndex(t => t.Id == track.Id);
        if (index >= 0) Data.Tracks[index] = track;
        else Data.Tracks.Add(track);
    }

    public static bool IsAudio(string path) => AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Добавляет локальные файлы. Возвращает число добавленных треков.</summary>
    public async Task<int> AddFilesAsync(IEnumerable<string> files, IProgress<string>? progress = null,
        TrackInfoDraft? info = null)
    {
        var list = files.Where(IsAudio).ToList();
        int total = list.Count;
        int added = 0;
        foreach (var source in list)
        {
            progress?.Report(Path.GetFileName(source));

            if (Data.Tracks.Any(t => string.Equals(t.FileName, source, StringComparison.OrdinalIgnoreCase)))
                continue;

            var track = new TrackItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = Path.GetFileNameWithoutExtension(source),
                AddedAt = DateTime.Now,
                // Сразу попадает и в приложение, и в веб-плеер: отдельная кнопка
                // «Опубликовать» больше не нужна.
                IsPublished = true
            };

            try
            {
                await Task.Run(() => ReadTags(source, track));
            }
            catch
            {
                // Если теги не читаются — остаёмся на имени файла.
            }

            ApplyDraft(track, info, total);

            var ext = Path.GetExtension(source).ToLowerInvariant();
            var newPath = Path.Combine(AppPaths.Tracks, track.Id + ext);
            await Task.Run(() => File.Copy(source, newPath, true));
            track.FileName = newPath;

            var siblingVideo = Path.ChangeExtension(source, ".mp4");
            if (File.Exists(siblingVideo))
            {
                var videoPath = Path.Combine(AppPaths.Videos, track.Id + ".mp4");
                await Task.Run(() => File.Copy(siblingVideo, videoPath, true));
                track.VideoFile = videoPath;
            }

            Data.Tracks.Add(track);
            added++;
        }

        if (added > 0) Save();
        return added;
    }

    /// <summary>
    /// Возвращает в библиотеку файлы, которые уже лежат в папке Tracks, но потерялись
    /// в library.json (например, после восстановления или очистки). Ничего не удаляет.
    /// </summary>
    public int ReconcileFromDisk()
    {
        if (!Directory.Exists(AppPaths.Tracks)) return 0;
        var known = new HashSet<string>(Data.Tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.FileName))
            .Select(t => Path.GetFullPath(t.FileName)), StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (string source in Directory.EnumerateFiles(AppPaths.Tracks))
        {
            if (!IsAudio(source)) continue;
            string full = Path.GetFullPath(source);
            if (known.Contains(full)) continue;

            var track = new TrackItem
            {
                Id = Path.GetFileNameWithoutExtension(source),
                FileName = source,
                Title = Path.GetFileNameWithoutExtension(source),
                AddedAt = File.GetLastWriteTime(source),
                IsPublished = true
            };
            if (Data.Tracks.Any(t => t.Id == track.Id)) continue;

            try { ReadTags(source, track); } catch { }

            // Один и тот же трек мог лежать в папке несколькими копиями с разными именами.
            // Такой файл не добавляем повторно — иначе дубли снова появятся и в вебе.
            if (Data.Tracks.Any(t => LooksDuplicate(t, track)))
            {
                known.Add(full);
                continue;
            }

            string cover = Path.Combine(AppPaths.Covers, track.Id + ".jpg");
            if (File.Exists(cover)) track.CoverFile = cover;
            string lyrics = Path.Combine(AppPaths.Lyrics, track.Id + ".lrc");
            if (File.Exists(lyrics)) track.LyricsFile = lyrics;

            Data.Tracks.Add(track);
            known.Add(full);
            added++;
        }

        if (added > 0) Save();
        return added;
    }

    private static bool LooksDuplicate(TrackItem a, TrackItem b)
    {
        if (string.IsNullOrWhiteSpace(a.Title) || string.IsNullOrWhiteSpace(b.Title)) return false;
        if (!string.Equals(a.Title.Trim(), b.Title.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals((a.Artist ?? "").Trim(), (b.Artist ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return Math.Abs(a.DurationSeconds - b.DurationSeconds) < 1.0;
    }

    /// <summary>
    /// Накладывает данные из окна «Информация о треке» на добавленный трек.
    /// Название — только для одиночного файла, остальное — если выбрано «применить ко всем».
    /// </summary>
    private static void ApplyDraft(TrackItem track, TrackInfoDraft? info, int total)
    {
        if (info is null) return;
        bool applyAll = info.ApplyToAll || total == 1;
        if (!applyAll) return;

        if (total == 1 && !string.IsNullOrWhiteSpace(info.Title)) track.Title = info.Title.Trim();
        if (!string.IsNullOrWhiteSpace(info.Artist)) track.Artist = info.Artist.Trim();
        if (!string.IsNullOrWhiteSpace(info.Album)) track.Album = info.Album.Trim();
        if (!string.IsNullOrWhiteSpace(info.Comment)) track.Comment = info.Comment.Trim();
        if (info.AllowExternal) track.AllowExternal = true;
    }

    /// <summary>Обновляет метаданные уже добавленного трека (из окна информации).</summary>
    public void UpdateInfo(TrackItem track, TrackInfoDraft info)
    {
        if (!string.IsNullOrWhiteSpace(info.Title)) track.Title = info.Title.Trim();
        if (!string.IsNullOrWhiteSpace(info.Artist)) track.Artist = info.Artist.Trim();
        track.Album = info.Album.Trim();
        track.Comment = info.Comment.Trim();
        track.AllowExternal = info.AllowExternal;
        Save();
    }

    public static void ReadTags(string path, TrackItem track)
    {
        using var file = global::TagLib.File.Create(path);
        if (!string.IsNullOrWhiteSpace(file.Tag.Title)) track.Title = file.Tag.Title;
        if (!string.IsNullOrWhiteSpace(file.Tag.FirstPerformer)) track.Artist = file.Tag.FirstPerformer;
        if (!string.IsNullOrWhiteSpace(file.Tag.Album)) track.Album = file.Tag.Album;
        track.DurationSeconds = file.Properties.Duration.TotalSeconds;

        var picture = file.Tag.Pictures.FirstOrDefault();
        if (picture?.Data?.Data is { Length: > 0 } bytes)
        {
            var coverPath = Path.Combine(AppPaths.Covers, track.Id + ".jpg");
            File.WriteAllBytes(coverPath, bytes);
            track.CoverFile = coverPath;
        }
    }

    public TrackItem? Find(string id) => Data.Tracks.FirstOrDefault(t => t.Id == id);

    /// <summary>Убирает трек из списка, но не трогает сам файл на диске.</summary>
    public void RemoveFromLibrary(string id)
    {
        var track = Find(id);
        if (track is null) return;
        Data.Tracks.Remove(track);
        foreach (var playlist in Data.Playlists) playlist.TrackIds.Remove(id);
        Save();
    }

    public void Delete(string id)
    {
        var track = Find(id);
        if (track is null) return;

        TryDelete(track.FileName);
        TryDelete(track.CoverFile);
        TryDelete(track.VideoFile);
        TryDelete(track.LyricsFile);
        Data.Tracks.Remove(track);
        foreach (var playlist in Data.Playlists) playlist.TrackIds.Remove(id);
        Save();
    }

    private static void TryDelete(string? path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist { Name = string.IsNullOrWhiteSpace(name) ? "Новый плейлист" : name.Trim() };
        Data.Playlists.Add(playlist);
        Save();
        return playlist;
    }

    public void AddToPlaylist(string playlistId, string trackId)
    {
        var playlist = Data.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist is null || playlist.TrackIds.Contains(trackId)) return;
        playlist.TrackIds.Add(trackId);
        Save();
    }

    public void ToggleFavorite(string id)
    {
        var track = Find(id);
        if (track is null) return;
        track.IsFavorite = !track.IsFavorite;
        Save();
    }

    public void SetComment(string id, string comment)
    {
        var track = Find(id);
        if (track is null) return;
        track.Comment = comment.Trim();
        Save();
    }

    /// <summary>Перечитать library.json после восстановления бэкапа.</summary>
    public void Reload()
    {
        Load();
    }

    public void RegisterPlay(string id)
    {
        var track = Find(id);
        if (track is not null) track.PlayCount++;
        Data.TotalPlayed++;
        Save();
    }
}
