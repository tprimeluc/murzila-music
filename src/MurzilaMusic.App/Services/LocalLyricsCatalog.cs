using System.Text.RegularExpressions;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Локальный каталог текстов. Мгновенно ищет .lrc в папке «Тексты» пользователя,
/// не обращаясь к интернету. Это надёжный запасной путь, когда lrclib недоступен.
/// </summary>
public sealed class LocalLyricsCatalog
{
    private static readonly Lazy<LocalLyricsCatalog> Lazy = new(() => new LocalLyricsCatalog());
    public static LocalLyricsCatalog Default => Lazy.Value;

    /// <summary>Принудительно пересканировать папку (например, после добавления файла).</summary>
    public void Refresh()
    {
        lock (_sync) _lastScan = DateTime.MinValue;
    }

    private sealed class Entry
    {
        public string Path = "";
        public string Title = "";
        public string Artist = "";
        public string TitleKey = "";
        public string ArtistKey = "";
        public string FileKey = "";
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, LyricsDocument> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private DateTime _lastScan = DateTime.MinValue;

    public LyricsDocument? Find(TrackItem track)
    {
        EnsureScanned();
        string titleKey = Normalize(track.Title);
        string artistKey = Normalize(track.Artist);
        string fileKey = Normalize(Path.GetFileNameWithoutExtension(track.FileName));

        Entry? best = null;
        int bestScore = 0;

        lock (_sync)
        {
            foreach (var entry in _entries)
            {
                int score = Score(titleKey, artistKey, fileKey, entry);
                if (score > bestScore)
                {
                    best = entry;
                    bestScore = score;
                }
            }
        }

        if (best is null || bestScore < 30) return null;

        try
        {
            lock (_sync)
            {
                if (_cache.TryGetValue(best.Path, out var cached))
                    return cached;

                var document = LyricsService.Parse(File.ReadAllText(best.Path));
                if (document is null) return null;
                document.Source = "Локальная папка текстов";
                _cache[best.Path] = document;
                return document;
            }
        }
        catch
        {
            return null;
        }
    }

    private static int Score(string titleKey, string artistKey, string fileKey, Entry entry)
    {
        bool titleMatch = titleKey.Length > 0 && titleKey == entry.TitleKey;
        bool artistMatch = artistKey.Length > 0 && artistKey == entry.ArtistKey;
        bool fileMatch = fileKey.Length > 0 && fileKey == entry.FileKey;

        // Точное совпадение «артист + название» — самый надёжный вариант.
        if (titleMatch && artistMatch) return 100;
        // Точное имя файла тоже надёжно (часто «Артист - Название.lrc»).
        if (fileMatch) return 90;
        // Название без известного артиста — тоже ок, но слабее.
        if (titleMatch && (artistKey.Length == 0 || entry.ArtistKey.Length == 0)) return 70;

        // Нечёткие совпадения допускаем только когда артист неизвестен,
        // иначе демо-текст мог бы прилипнуть к любой песне с таким названием.
        if (artistKey.Length > 0 && entry.ArtistKey.Length > 0 && !artistMatch) return 0;
        if (titleKey.Length > 0 && (entry.TitleKey.Contains(titleKey) || titleKey.Contains(entry.TitleKey)))
            return 40;
        if (fileKey.Length > 0 && entry.FileKey.Length > 0
            && (entry.FileKey.Contains(fileKey) || fileKey.Contains(entry.FileKey)))
            return 30;
        return 0;
    }

    private void EnsureScanned()
    {
        lock (_sync)
        {
            if (DateTime.UtcNow - _lastScan < TimeSpan.FromSeconds(10)) return;

            _entries.Clear();
            _cache.Clear();

            try
            {
                foreach (string path in Directory.EnumerateFiles(AppPaths.Lyrics, "*.lrc"))
                {
                    var (title, artist) = ReadMeta(path);
                    string fileName = Path.GetFileNameWithoutExtension(path);

                    if (fileName.Contains(" - ", StringComparison.Ordinal))
                    {
                        string[] parts = fileName.Split(new[] { " - " }, 2, StringSplitOptions.None);
                        if (string.IsNullOrWhiteSpace(artist)) artist = parts[0].Trim();
                        if (string.IsNullOrWhiteSpace(title)) title = parts[1].Trim();
                    }
                    if (string.IsNullOrWhiteSpace(title)) title = fileName;
                    if (string.IsNullOrWhiteSpace(artist)) artist = "";

                    _entries.Add(new Entry
                    {
                        Path = path,
                        Title = title,
                        Artist = artist,
                        TitleKey = Normalize(title),
                        ArtistKey = Normalize(artist),
                        FileKey = Normalize(fileName)
                    });
                }
            }
            catch
            {
                // Папка текстов может быть временно недоступна — просто работаем без каталога.
            }

            _lastScan = DateTime.UtcNow;
        }
    }

    private static (string Title, string Artist) ReadMeta(string path)
    {
        string title = "", artist = "";
        try
        {
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[ti:", StringComparison.OrdinalIgnoreCase))
                    title = line[4..].Trim().TrimEnd(']');
                else if (line.StartsWith("[ar:", StringComparison.OrdinalIgnoreCase))
                    artist = line[4..].Trim().TrimEnd(']');
            }
        }
        catch { }
        return (title, artist);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string s = value.ToLowerInvariant();
        s = Regex.Replace(s, @"\s+(?:feat\.?|ft\.?|featuring)\s+.*$", " ");
        s = Regex.Replace(s, @"[\(\[].*?[\)\]]", " ");
        s = Regex.Replace(s, @"[^\p{L}\p{N}]+", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
