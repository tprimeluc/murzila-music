using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// Клиент локального Python-помощника: история, закладки, перевод.
/// Если помощник не запущен, браузер работает без него — это не критично.
/// </summary>
public sealed class PythonBridge : IDisposable
{
    private readonly BrowserPrefs _settings;
    private readonly HttpClient _http;
    private Process? _process;

    public bool Connected { get; private set; }

    public PythonBridge(BrowserPrefs settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task StartAsync()
    {
        if (!_settings.EnablePythonSidecar) return;
        string script = Path.Combine(AppContext.BaseDirectory, "sidecar", "server.py");
        if (File.Exists(script))
        {
            try
            {
                var psi = new ProcessStartInfo("python")
                {
                    ArgumentList = { script, _settings.SidecarPort.ToString() },
                    WorkingDirectory = Path.GetDirectoryName(script)!,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _process = Process.Start(psi);
            }
            catch { }
        }
        await PingAsync();
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var r = await _http.GetAsync($"http://127.0.0.1:{_settings.SidecarPort}/api/health");
            Connected = r.IsSuccessStatusCode;
        }
        catch { Connected = false; }
        return Connected;
    }

    public async Task AddHistoryAsync(string url, string title)
    {
        if (!await EnsureAsync()) return;
        await PostAsync("/api/history", new { url, title });
    }

    public async Task<JsonElement> GetHistoryAsync(int limit = 40)
    {
        return await GetAsync($"/api/history?limit={limit}");
    }

    public async Task AddBookmarkAsync(string url, string title)
    {
        if (!await EnsureAsync()) return;
        await PostAsync("/api/bookmarks", new { url, title });
    }

    public async Task<JsonElement> GetBookmarksAsync()
    {
        return await GetAsync("/api/bookmarks");
    }

    public async Task<string?> TranslateAsync(string text, string lang)
    {
        if (!await EnsureAsync()) return null;
        try
        {
            var response = await PostAsync("/api/translate", new { text, lang });
            using var doc = JsonDocument.Parse(response);
            return doc.RootElement.TryGetProperty("translated", out var value) ? value.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<bool> EnsureAsync()
    {
        if (Connected) return true;
        return await PingAsync();
    }

    private async Task<string> PostAsync(string path, object payload)
    {
        string json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"http://127.0.0.1:{_settings.SidecarPort}{path}", content);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<JsonElement> GetAsync(string path)
    {
        try
        {
            string json = await _http.GetStringAsync($"http://127.0.0.1:{_settings.SidecarPort}{path}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return JsonDocument.Parse("[]").RootElement.Clone(); }
    }

    public void Dispose()
    {
        try { if (_process is { HasExited: false }) _process.Kill(true); } catch { }
        _http.Dispose();
    }
}
