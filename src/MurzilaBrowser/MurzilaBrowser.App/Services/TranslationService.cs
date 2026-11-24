using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// Переводчик без Python и без ключей: Google -> MyMemory, кэш на диске.
/// Умеет пакетный перевод и автопредел языка.
/// </summary>
public sealed class TranslationService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private static string CachePath => Path.Combine(BrowserPrefs.AppDataPath, "translations.json");

    public TranslationService()
    {
        try
        {
            if (File.Exists(CachePath))
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath)) ?? new())
                    _cache[pair.Key] = pair.Value;
        }
        catch { }
    }

    public string? DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Any(c => c >= '\u0400' && c <= '\u04FF')) return "ru";
        return "en";
    }

    public async Task<string?> TranslateAsync(string text, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text) || targetLang == "ru") return text;
        string key = targetLang + "|" + text;
        if (_cache.TryGetValue(key, out string? cached)) return cached;

        string? result = await GoogleAsync(text, targetLang);
        if (result is null) result = await MyMemoryAsync(text, targetLang);
        if (result is not null)
        {
            _cache[key] = result;
            try { File.WriteAllText(CachePath, JsonSerializer.Serialize(_cache)); } catch { }
        }
        return result;
    }

    public async Task<Dictionary<string, string>> TranslateBatchAsync(IEnumerable<string> texts, string targetLang)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string text in texts.Distinct().Take(120))
        {
            string? value = await TranslateAsync(text, targetLang);
            if (value is not null) result[text] = value;
        }
        return result;
    }

    private async Task<string?> GoogleAsync(string text, string lang)
    {
        try
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl="
                + Uri.EscapeDataString(lang) + "&dt=t&q=" + Uri.EscapeDataString(text);
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url));
            return doc.RootElement[0][0][0].GetString();
        }
        catch { return null; }
    }

    private async Task<string?> MyMemoryAsync(string text, string lang)
    {
        try
        {
            string source = DetectLanguage(text) ?? "ru";
            string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text)
                + "&langpair=" + Uri.EscapeDataString(source + "|" + lang);
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(url));
            string? value = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
            return value?.StartsWith("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) == true ? null : value;
        }
        catch { return null; }
    }
}
