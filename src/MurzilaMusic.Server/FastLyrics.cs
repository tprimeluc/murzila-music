using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MurzilaMusic.Server;

/// <summary>Результат одного провайдера в быстром поиске текста.</summary>
public sealed record LyricsHit(string Provider, long Ms, bool Ok, bool Synced, string Text);

/// <summary>Итог быстрого поиска: текст победителя + время всех провайдеров.</summary>
public sealed record LyricsSearchResult(
    string? Text, bool Synced, string Source, long ElapsedMs, List<LyricsHit> Providers);

/// <summary>
/// Параллельный поиск текста песни по LrcLib, Genius, Musixmatch и lyrics.ovh.
/// Все провайдеры опрашиваются одновременно — результат приходит за время
/// самого быстрого из них (миллисекунды при хорошем интернете, секунды при плохом).
/// </summary>
public sealed class FastLyrics
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
        DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("MurzilaMusic", "3.9") } }
    };

    private readonly string _geniusToken = Environment.GetEnvironmentVariable("GENIUS_ACCESS_TOKEN") ?? "";
    private readonly string _musixmatchKey = Environment.GetEnvironmentVariable("MUSIXMATCH_API_KEY") ?? "";

    public async Task<LyricsSearchResult> SearchAsync(string artist, string title, double duration = 0)
    {
        artist = (artist ?? "").Trim();
        title = (title ?? "").Trim();
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task<LyricsHit>>
        {
            Hit("lrclib", () => FetchLrclibAsync(artist, title, duration)),
            Hit("genius", () => FetchGeniusAsync(artist, title)),
            Hit("lyrics.ovh", () => FetchLyricsOvhAsync(artist, title))
        };
        if (!string.IsNullOrWhiteSpace(_musixmatchKey))
            tasks.Add(Hit("musixmatch", () => FetchMusixmatchAsync(artist, title)));

        LyricsHit[] hits;
        try { hits = await Task.WhenAll(tasks); }
        catch { hits = Array.Empty<LyricsHit>(); }
        sw.Stop();

        var good = hits.Where(h => h.Ok && !string.IsNullOrWhiteSpace(h.Text)).ToList();
        LyricsHit? winner = good.Count == 0 ? null
            : good.OrderByDescending(h => h.Synced).ThenBy(h => h.Ms).First();

        // Текст победителя отдаём клиенту, остальным — только тайминги.
        var report = hits.Select(h => new LyricsHit(h.Provider, h.Ms, h.Ok, h.Synced,
            ReferenceEquals(h, winner) ? h.Text : "")).OrderBy(h => h.Ms).ToList();

        return new LyricsSearchResult(winner?.Text, winner?.Synced ?? false,
            winner?.Provider ?? "", sw.ElapsedMilliseconds, report);
    }

    private static async Task<LyricsHit> Hit(string provider, Func<Task<string?>> fetch)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            string? text = await fetch();
            sw.Stop();
            return new LyricsHit(provider, sw.ElapsedMilliseconds,
                !string.IsNullOrWhiteSpace(text), LooksSynced(text), text ?? "");
        }
        catch { sw.Stop(); return new LyricsHit(provider, sw.ElapsedMilliseconds, false, false, ""); }
    }

    private static bool LooksSynced(string? text)
        => !string.IsNullOrWhiteSpace(text) && Regex.IsMatch(text, @"\[\d{1,3}:\d{2}(?:\.\d+)?\]");

    private static string StripTags(string html)
    {
        html = Regex.Replace(html, @"<br\s*/?>|</p>|</div>", "\n", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", "");
        return WebUtility.HtmlDecode(html).Trim();
    }

    // 1. LrcLib — синхронизированный LRC, самый ценный результат.
    private static async Task<string?> FetchLrclibAsync(string artist, string title, double duration)
    {
        string clean = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();
        var variants = new List<(string Artist, string Title)> { (artist, title) };
        if (clean.Length > 2 && !string.Equals(clean, title, StringComparison.OrdinalIgnoreCase))
            variants.Add((artist, clean));
        if (string.IsNullOrWhiteSpace(artist)) variants.Add(("", title));

        foreach (var v in variants)
        {
            string query = "https://lrclib.net/api/search?artist_name=" + Uri.EscapeDataString(v.Artist)
                         + "&track_name=" + Uri.EscapeDataString(v.Title);
            using var response = await Http.GetAsync(query);
            if (!response.IsSuccessStatusCode) continue;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

            string? plain = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!string.IsNullOrWhiteSpace(v.Artist) && item.TryGetProperty("artistName", out var an))
                {
                    string got = an.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(got) &&
                        !string.Equals(got.Trim(), v.Artist.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (duration > 0 && item.TryGetProperty("duration", out var dn) && dn.TryGetDouble(out double d)
                    && d > 0 && Math.Abs(d - duration) > 20) continue;
                if (item.TryGetProperty("syncedLyrics", out var s) && s.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(s.GetString())) return s.GetString();
                if (plain is null && item.TryGetProperty("plainLyrics", out var p)
                    && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                    plain = p.GetString();
            }
            if (!string.IsNullOrWhiteSpace(plain)) return plain;
        }
        return null;
    }

    // 2. Genius — поиск + лёгкий парсинг страницы. Токен необязателен.
    private async Task<string?> FetchGeniusAsync(string artist, string title)
    {
        string q = string.IsNullOrWhiteSpace(artist) ? title : title + " " + artist;
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.genius.com/search?q=" + Uri.EscapeDataString(q));
        if (!string.IsNullOrWhiteSpace(_geniusToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _geniusToken);
        using var search = await Http.SendAsync(req);
        if (!search.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        var hits = doc.RootElement.GetProperty("response").GetProperty("hits");
        string? url = null;
        foreach (var h in hits.EnumerateArray())
        {
            if (!h.TryGetProperty("result", out var r) || !r.TryGetProperty("url", out var u)) continue;
            url = u.GetString();
            if (!string.IsNullOrWhiteSpace(url)) break;
        }
        if (string.IsNullOrWhiteSpace(url)) return null;

        using var page = await Http.GetAsync(url);
        if (!page.IsSuccessStatusCode) return null;
        string html = await page.Content.ReadAsStringAsync();
        var containers = Regex.Matches(html,
            @"<div[^>]*data-lyrics-container=""true""[^>]*>(.*?)</div>", RegexOptions.Singleline);
        if (containers.Count == 0) return null;
        string text = string.Join("\n", containers.Select(m => StripTags(m.Groups[1].Value)));
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length > 0 ? text : null;
    }

    // 3. Musixmatch — официальный публичный API, нужен apikey (MUSIXMATCH_API_KEY).
    private async Task<string?> FetchMusixmatchAsync(string artist, string title)
    {
        string q = "https://api.musixmatch.com/ws/1.1/matcher.lyrics.get?format=json"
                 + "&q_track=" + Uri.EscapeDataString(title)
                 + "&q_artist=" + Uri.EscapeDataString(artist)
                 + "&apikey=" + Uri.EscapeDataString(_musixmatchKey);
        using var response = await Http.GetAsync(q);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = doc.RootElement.GetProperty("message").GetProperty("body");
        if (!body.TryGetProperty("lyrics", out var lyrics)) return null;
        string? text = lyrics.GetProperty("lyrics_body").GetString();
        if (string.IsNullOrWhiteSpace(text)) return null;
        int cut = text.IndexOf("*******", StringComparison.Ordinal);
        if (cut > 0) text = text[..cut];
        return text.Trim();
    }

    // 4. lyrics.ovh — запасной обычный текст.
    private static async Task<string?> FetchLyricsOvhAsync(string artist, string title)
    {
        string clean = Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "").Trim();
        string url = "https://api.lyrics.ovh/v1/" + Uri.EscapeDataString(artist) + "/" +
                     Uri.EscapeDataString(string.IsNullOrWhiteSpace(clean) ? title : clean);
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? text = doc.RootElement.TryGetProperty("lyrics", out var l) ? l.GetString() : null;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
