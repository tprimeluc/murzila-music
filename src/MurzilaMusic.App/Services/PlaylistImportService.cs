using System.Text.Json;
using System.Xml.Linq;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Импорт только своих плейлистов из файла: M3U/M3U8, JSON или XML со списком ЛОКАЛЬНЫХ файлов.
/// Ничего не скачивается с Яндекс Музыки или других сервисов — только файлы, уже лежащие на этом ПК.
/// </summary>
public static class PlaylistImportService
{
    private sealed record Entry(string? Path, string? Title, string? Artist);

    public sealed record Result(string PlaylistName, int Added, int Matched, int Skipped);

    public static async Task<Result> ImportAsync(LibraryService library, string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Файл плейлиста не найден.", file);
        string ext = Path.GetExtension(file).ToLowerInvariant();
        List<Entry> entries = ext switch
        {
            ".m3u" or ".m3u8" => ReadM3u(file),
            ".json" => ReadJson(file),
            ".xml" or ".pls" => ReadXml(file),
            _ => throw new InvalidOperationException("Поддерживаются M3U, M3U8, JSON и XML плейлисты.")
        };

        var playlist = library.CreatePlaylist(Path.GetFileNameWithoutExtension(file));
        int added = 0, matched = 0, skipped = 0;
        string? pendingTitle = null, pendingArtist = null;

        foreach (var entry in entries)
        {
            string? path = entry.Path;
            if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && File.Exists(path)
                && LibraryService.IsAudio(path))
            {
                int before = library.Tracks.Count;
                await library.AddFilesAsync(new[] { path });
                var track = library.Tracks.Skip(before).FirstOrDefault()
                            ?? library.Tracks.LastOrDefault(t => t.FileName == path);
                if (track is not null)
                {
                    library.AddToPlaylist(playlist.Id, track.Id);
                    added++;
                    pendingTitle = pendingArtist = null;
                    continue;
                }
            }

            string title = entry.Title ?? pendingTitle ?? "";
            string artist = entry.Artist ?? pendingArtist ?? "";
            pendingTitle = pendingArtist = null;

            var existing = library.Tracks.FirstOrDefault(t =>
                (!string.IsNullOrWhiteSpace(title) && t.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
                && (!string.IsNullOrWhiteSpace(artist) || t.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase)));
            if (existing is not null)
            {
                library.AddToPlaylist(playlist.Id, existing.Id);
                matched++;
            }
            else skipped++;
        }

        return new Result(playlist.Name, added, matched, skipped);
    }

    private static List<Entry> ReadM3u(string file)
    {
        var result = new List<Entry>();
        string? title = null, artist = null;
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                string info = line[8..];
                int comma = info.IndexOf(',');
                string names = comma > 0 ? info[(comma + 1)..] : info;
                string[] parts = names.Split(" - ", 2, StringSplitOptions.TrimEntries);
                artist = parts.Length == 2 ? parts[0] : null;
                title = parts.Length == 2 ? parts[1] : parts[0];
                continue;
            }
            if (line.StartsWith('#')) continue;
            result.Add(new Entry(line, title, artist));
            title = artist = null;
        }
        return result;
    }

    private static List<Entry> ReadJson(string file)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        var root = json.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray()
            : root.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array
                ? tracks.EnumerateArray()
                : Enumerable.Empty<JsonElement>();

        var result = new List<Entry>();
        foreach (var item in array)
        {
            string? path = GetString(item, "path", "file", "url", "FileName");
            string? title = GetString(item, "title", "name", "Title");
            string? artist = GetString(item, "artist", "performer", "Artist");
            result.Add(new Entry(path, title, artist));
        }
        return result;
    }

    private static List<Entry> ReadXml(string file)
    {
        var doc = XDocument.Load(file);
        var result = new List<Entry>();
        foreach (var node in doc.Descendants().Where(e =>
                     e.Name.LocalName.Equals("track", StringComparison.OrdinalIgnoreCase)))
        {
            string? path = Value(node, "path", "file", "url", "location");
            string? title = Value(node, "title", "name");
            string? artist = Value(node, "artist", "performer");
            result.Add(new Entry(path, title, artist));
        }
        return result;
    }

    private static string? GetString(JsonElement item, params string[] names)
    {
        foreach (string name in names)
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static string? Value(XElement node, params string[] names)
    {
        foreach (string name in names)
        {
            var element = node.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (element is not null && !string.IsNullOrWhiteSpace(element.Value)) return element.Value;
        }
        return null;
    }
}
