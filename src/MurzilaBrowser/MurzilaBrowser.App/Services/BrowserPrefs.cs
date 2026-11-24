using System.IO;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

public sealed class BrowserPrefs
{
    public string HomeUrl { get; set; } = "murzila://home";
    public string MusicUrl { get; set; } = "http://localhost:6001/player";
    public string SearchUrl { get; set; } = "https://duckduckgo.com/?q={0}";
    public bool StartOnHomePage { get; set; } = true;
    public bool EnablePythonSidecar { get; set; } = true;
    public int SidecarPort { get; set; } = 6137;

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
