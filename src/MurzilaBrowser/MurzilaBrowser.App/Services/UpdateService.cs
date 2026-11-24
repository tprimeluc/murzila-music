using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// Обновления прямо в приложении: проверка GitHub Releases и скачивание MSI.
/// Требует интернет.
/// </summary>
public sealed class UpdateService
{
    private const string Repo = "tprimeluc/murzila-music";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MurzilaBrowser");
            using var doc = JsonDocument.Parse(await _http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string html = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            string? asset = null;
            foreach (var item in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = item.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) && name.Contains("Browser", StringComparison.OrdinalIgnoreCase))
                {
                    asset = item.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            return new UpdateInfo
            {
                LatestTag = tag.TrimStart('v'),
                CurrentVersion = CurrentVersion,
                ReleaseUrl = html,
                AssetUrl = asset
            };
        }
        catch { return null; }
    }

    public async Task<string?> DownloadAsync(string url)
    {
        try
        {
            string name = Path.GetFileName(new Uri(url).LocalPath);
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MurzilaBrowser-" + name);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(path);
            await stream.CopyToAsync(file);
            return path;
        }
        catch { return null; }
    }
}

public sealed class UpdateInfo
{
    public string LatestTag { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string ReleaseUrl { get; set; } = "";
    public string? AssetUrl { get; set; }
    public bool IsNewer => Version.TryParse(LatestTag, out var latest) && Version.TryParse(CurrentVersion, out var current) && latest > current;
}
