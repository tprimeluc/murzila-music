using System.IO;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// История и закладки хранятся прямо в C#, без Python и без внешних процессов.
/// </summary>
public sealed class LocalDataService
{
    private static string Root => BrowserPrefs.AppDataPath;
    private static string HistoryPath => Path.Combine(Root, "history.json");
    private static string BookmarksPath => Path.Combine(Root, "bookmarks.json");

    public List<HistoryEntry> History { get; private set; } = new();
    public List<BookmarkEntry> Bookmarks { get; private set; } = new();

    public LocalDataService()
    {
        Directory.CreateDirectory(Root);
        History = Load<HistoryEntry>(HistoryPath);
        Bookmarks = Load<BookmarkEntry>(BookmarksPath);
    }

    private static List<T> Load<T>(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? new List<T>();
        }
        catch { }
        return new List<T>();
    }

    private static void Save<T>(string path, List<T> value)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    public void AddHistory(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        History.RemoveAll(x => x.Url == url);
        History.Add(new HistoryEntry { Url = url, Title = title, Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        if (History.Count > 5000) History = History.TakeLast(5000).ToList();
        Save(HistoryPath, History);
    }

    public void AddBookmark(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || Bookmarks.Any(x => x.Url == url)) return;
        Bookmarks.Add(new BookmarkEntry { Url = url, Title = title, Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        Save(BookmarksPath, Bookmarks);
    }

    public void RemoveBookmark(string url)
    {
        Bookmarks.RemoveAll(x => x.Url == url);
        Save(BookmarksPath, Bookmarks);
    }

    public void ClearHistory()
    {
        History.Clear();
        Save(HistoryPath, History);
    }
}

public sealed class HistoryEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public long Time { get; set; }
}

public sealed class BookmarkEntry
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public long Time { get; set; }
}
