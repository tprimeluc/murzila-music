using System.IO;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

public sealed class BrowserPrefs
{
    public string HomeUrl { get; set; } = "murzila://home";
    public string SearchEngine { get; set; } = "DuckDuckGo";
    public bool LowEndMode { get; set; } = false;
    public bool HardwareAcceleration { get; set; } = true;
    public bool FullscreenOnStart { get; set; } = false;
    public int DefaultZoomPercent { get; set; } = 100;
    public bool EnableVoiceTranslation { get; set; } = true;
    public bool EnableLiveVoices { get; set; } = true;
    public bool EnableInAppUpdates { get; set; } = true;
    public bool MaxPerformance { get; set; } = false;
    public bool DisableVSync { get; set; } = false;
    public int MaxFps { get; set; } = 100000;
    public bool MillionObjectsMode { get; set; } = false;

    public static IReadOnlyDictionary<string, string> SearchEngines => new Dictionary<string, string>
    {
        ["DuckDuckGo"] = "https://duckduckgo.com/?q={0}",
        ["Google"] = "https://www.google.com/search?q={0}",
        ["Bing"] = "https://www.bing.com/search?q={0}",
        ["Yandex"] = "https://yandex.ru/search/?text={0}",
        ["Brave"] = "https://search.brave.com/search?q={0}",
        ["Ecosia"] = "https://www.ecosia.org/search?q={0}"
    };

    public string SearchUrl => SearchEngines.TryGetValue(SearchEngine, out string? url)
        ? url : SearchEngines["DuckDuckGo"];

    public static string AppDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurzilaBrowser");

    public static string ProfilePath => Path.Combine(AppDataPath, "Profile");

    private static string SettingsPath => Path.Combine(AppDataPath, "settings.json");

    public static BrowserPrefs Load()
    {
        Directory.CreateDirectory(ProfilePath);
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<BrowserPrefs>(File.ReadAllText(SettingsPath)) ?? new BrowserPrefs();
        }
        catch { }
        return new BrowserPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
