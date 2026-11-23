using MurzilaMusic.App.Services;

namespace MurzilaMusic.App.Models;

/// <summary>
/// Трек в локальной библиотеке. Источник только один — файлы, которые добавил сам пользователь.
/// </summary>
public sealed class TrackItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Без названия";
    public string Artist { get; set; } = "Неизвестный артист";
    public string Album { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? CoverFile { get; set; }
    public string? VideoFile { get; set; }
    public string? LyricsFile { get; set; }

    /// <summary>Своя заметка пользователя к треку. Никуда не отправляется.</summary>
    public string Comment { get; set; } = "";
    public double DurationSeconds { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public bool IsFavorite { get; set; }
    public bool IsPublished { get; set; }

    /// <summary>Артист сам разрешил другим слушать этот трек в Murzila Music.</summary>
    public bool AllowExternal { get; set; }
    public long PlayCount { get; set; }

    /// <summary>Артист со счётчиком прослушиваний в списке.</summary>
    public string DisplayArtist => SettingsService.Current.ShowPlayCounts && PlayCount > 0
        ? $"{Artist}   ·   {PlayCount}"
        : Artist;

    public string DisplayDuration
    {
        get
        {
            int s = (int)Math.Max(0, DurationSeconds);
            return $"{s / 60:D2}:{s % 60:D2}";
        }
    }
}

public sealed class Playlist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новый плейлист";
    public List<string> TrackIds { get; set; } = new();
}

public sealed class LibraryData
{
    public List<TrackItem> Tracks { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
    public long TotalPlayed { get; set; }
}

public enum Tariff
{
    Free = 0,
    Single5 = 5,
    Pro50 = 50,
    Premium105 = 105,
    Ultra200 = 200
}
