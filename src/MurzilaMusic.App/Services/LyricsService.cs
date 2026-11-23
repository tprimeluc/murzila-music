using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Тексты из локальной папки, тегов аудиофайла, LRCLIB и запасного API.
/// Поддерживаются строчные и пословные временные метки: каждое слово подсвечивается.
/// Локальные источники работают мгновенно, сетевые — с коротким таймаутом.
/// </summary>
public sealed class LyricsService
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex LineRegex = new(@"^\[(\d{1,4}):(\d{2}(?:[.:]\d{1,3})?)\]\s*(.*)$");
    private static readonly Regex WordTagRegex = new(@"<(\d{1,4}):(\d{2}(?:[.:]\d{1,3})?)>");
    private static readonly Regex BracketRegex = new(@"\s*[\(\[\{].*?[\)\]\}]", RegexOptions.Compiled);
    private static readonly Regex FeatRegex = new(@"\s+(?:feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed record Candidate(
        string? Synced, string? Plain, string SyncedSource, string PlainSource, int Score);

    private static HttpClient CreateClient()
    {
        // Короткий таймаут: текст должен появляться за несколько секунд или не появляться вовсе.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2.5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MurzilaMusicBeta/0.1 (+https://murzila-music.ru)");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<LyricsDocument?> LoadAsync(TrackItem track, bool forceRefresh = false)
    {
        AppPaths.Ensure();

        bool hasLocalLrc = !string.IsNullOrWhiteSpace(track.LyricsFile)
                           && track.LyricsFile.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                           && File.Exists(track.LyricsFile);

        string cacheFile = string.IsNullOrWhiteSpace(track.LyricsFile) || hasLocalLrc
            ? Path.Combine(AppPaths.Lyrics, track.Id + ".json")
            : track.LyricsFile;

        if (!forceRefresh && File.Exists(cacheFile))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<LyricsDocument>(File.ReadAllText(cacheFile));
                if (cached is not null && cached.Lines.Count > 0)
                {
                    track.LyricsFile = cacheFile;
                    return EnsureTiming(cached, track.DurationSeconds);
                }
            }
            catch { }
        }

        var local = TryLoadLocalLrc(track);
        if (local is not null)
        {
            track.LyricsFile = cacheFile;
            Cache(cacheFile, local);
            return EnsureTiming(local, track.DurationSeconds);
        }

        var embedded = TryLoadEmbedded(track.FileName);
        if (embedded is not null)
        {
            track.LyricsFile = cacheFile;
            Cache(cacheFile, embedded);
            return EnsureTiming(embedded, track.DurationSeconds);
        }

        // Быстрый офлайн-каталог: его не нужно ждать и он не зависит от сети.
        var catalog = LocalLyricsCatalog.Default.Find(track);
        if (catalog is not null)
        {
            track.LyricsFile = cacheFile;
            Cache(cacheFile, catalog);
            return EnsureTiming(catalog, track.DurationSeconds);
        }

        var online = await SearchOnlineAsync(track);
        if (online is not null)
        {
            track.LyricsFile = cacheFile;
            Cache(cacheFile, online);
        }
        return online is null ? null : EnsureTiming(online, track.DurationSeconds);
    }

    /// <summary>Ручной поиск в сети (кнопка «Найти текст»).</summary>
    public Task<LyricsDocument?> SearchOnlineAsync(TrackItem track)
        => SearchOnlineInternalAsync(track);

    private async Task<LyricsDocument?> SearchOnlineInternalAsync(TrackItem track)
    {
        var settings = SettingsService.Current;
        var tasks = new List<Task<Candidate>>();
        var queries = BuildQueries(track).Take(2).ToList();
        var first = queries[0];

        // Обычный поиск LRCLIB и точный /api/get — параллельно. Точный ответ с
        // enhanced-метками (<mm:ss.xx>) получает максимальный приоритет.
        if (settings.UseLrclib)
        {
            foreach (var query in queries)
            {
                tasks.Add(FetchLrclibAsync(query.Title, query.Artist, query.Duration));
                tasks.Add(FetchLrclibGetAsync(query.Title, query.Artist, query.Duration));
            }
        }
        if (!IsUnknownArtist(track.Artist))
        {
            tasks.Add(FetchLyricsOvhAsync(track.Title, track.Artist));

            string geniusToken = Environment.GetEnvironmentVariable("GENIUS_TOKEN") ?? settings.GeniusToken;
            if (settings.UseGenius && !string.IsNullOrWhiteSpace(geniusToken))
                tasks.Add(FetchGeniusAsync(first.Title, first.Artist ?? track.Artist, geniusToken));
            if (settings.UseMusixmatch && !string.IsNullOrWhiteSpace(settings.MusixmatchToken))
                tasks.Add(FetchMusixmatchAsync(first.Title, first.Artist ?? track.Artist, settings.MusixmatchToken));
        }

        Candidate? best = null;
        var remaining = new List<Task<Candidate>>(tasks);
        while (remaining.Count > 0)
        {
            var finished = await Task.WhenAny(remaining);
            remaining.Remove(finished);

            Candidate candidate;
            try { candidate = await finished; }
            catch { continue; }

            if (best is null || candidate.Score > best.Score) best = candidate;
            // Enhanced-синхронизация найдена — ждать остальные запросы уже незачем.
            if (best.Score >= 1000) break;
        }

        if (best is null) return null;

        if (!string.IsNullOrWhiteSpace(best.Synced))
        {
            var document = Parse(best.Synced);
            if (document is not null)
            {
                document.Source = best.SyncedSource;
                return document;
            }
        }

        if (!string.IsNullOrWhiteSpace(best.Plain))
        {
            var document = ParsePlain(best.Plain, track.DurationSeconds);
            if (document is not null) document.Source = best.PlainSource;
            return document;
        }

        return null;
    }

    private static IEnumerable<(string Title, string? Artist, int? Duration)> BuildQueries(TrackItem track)
    {
        string title = track.Title.Trim();
        string artist = track.Artist.Trim();

        // Файл без тегов часто называется «Артист - Название».
        if (IsUnknownArtist(artist) && title.Contains(" - ", StringComparison.Ordinal))
        {
            string[] parts = title.Split(new[] { " - " }, 2, StringSplitOptions.None);
            artist = parts[0].Trim();
            title = parts[1].Trim();
        }

        bool hasArtist = !IsUnknownArtist(artist);
        string cleanTitle = CleanTitle(title);
        string cleanArtist = CleanArtist(artist);
        int? duration = track.DurationSeconds > 1 ? (int)Math.Round(track.DurationSeconds) : null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, string?, int?)>();

        void Add(string t, string? a, int? d)
        {
            if (string.IsNullOrWhiteSpace(t)) return;
            string key = $"{t}\u001f{a}\u001f{d}";
            if (seen.Add(key)) result.Add((t, string.IsNullOrWhiteSpace(a) ? null : a, d));
        }

        Add(cleanTitle, hasArtist ? cleanArtist : null, duration);
        Add(title, hasArtist ? artist : null, null);
        Add(cleanTitle, hasArtist ? cleanArtist : null, null);
        Add(cleanTitle, null, null);

        return result;
    }

    private static bool IsUnknownArtist(string artist) =>
        string.IsNullOrWhiteSpace(artist)
        || artist.Equals(Txt.LocalFileArtist, StringComparison.OrdinalIgnoreCase)
        || artist.Equals("Неизвестный артист", StringComparison.OrdinalIgnoreCase);

    private static string CleanTitle(string title)
    {
        string result = BracketRegex.Replace(title, " ");
        result = FeatRegex.Replace(result, " ");
        return Regex.Replace(result, @"\s{2,}", " ").Trim();
    }

    private static string CleanArtist(string artist) => FeatRegex.Replace(artist, "").Trim();

    private static async Task<Candidate> FetchLrclibAsync(string title, string? artist, int? duration)
    {
        try
        {
            string url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title);
            if (!string.IsNullOrWhiteSpace(artist))
                url += "&artist_name=" + Uri.EscapeDataString(artist);
            if (duration is > 0)
                url += "&duration=" + duration.Value;

            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return Empty();
            return FromLrclibJson(await response.Content.ReadAsStringAsync(), 0, duration,
                SettingsService.Current.PreferEnhancedLyrics, artist);
        }
        catch { return Empty(); }
    }

    /// <summary>Точный ответ LRCLIB: один трек, без догадок по имени.</summary>
    private static async Task<Candidate> FetchLrclibGetAsync(string title, string? artist, int? duration)
    {
        try
        {
            string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title);
            if (!string.IsNullOrWhiteSpace(artist))
                url += "&artist_name=" + Uri.EscapeDataString(artist);
            if (duration is > 0)
                url += "&duration=" + duration.Value;

            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return Empty();
            return FromLrclibJson(await response.Content.ReadAsStringAsync(), 200, duration,
                SettingsService.Current.PreferEnhancedLyrics, artist);
        }
        catch { return Empty(); }
    }

    private static Candidate Empty() => new(null, null, "", "", int.MinValue);

    /// <summary>
    /// Enhanced-LRC с пословными метками ценнее обычного построчного текста:
    /// именно по нему получается побуквенное попадание в пение.
    /// </summary>
    private static Candidate FromLrclibJson(string json, int exactBonus, int? duration, bool preferEnhanced,
        string? wantedArtist = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                return Empty();

            IEnumerable<JsonElement> items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                : new[] { document.RootElement };

            int wantedDuration = duration ?? 0;
            string? synced = null;
            string? plain = null;
            int syncScore = int.MinValue;
            int plainScore = int.MinValue;

            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                // Раньше по названию «360» подсовывали испанскую песню Альфреда Гарсии.
                if (!string.IsNullOrWhiteSpace(wantedArtist)
                    && wantedArtist.IndexOf("неизвест", StringComparison.OrdinalIgnoreCase) < 0
                    && item.TryGetProperty("artistName", out var an))
                {
                    string got = an.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(got)
                        && !string.Equals(got.Trim(), wantedArtist.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                double difference = 0;
                if (wantedDuration > 0 && item.TryGetProperty("duration", out var dv)
                    && dv.TryGetDouble(out double itemDuration) && itemDuration > 0)
                    difference = Math.Abs(itemDuration - wantedDuration);
                if (wantedDuration > 0 && difference > 20) continue;

                string s = item.TryGetProperty("syncedLyrics", out var sv) ? sv.GetString() ?? "" : "";
                string p = item.TryGetProperty("plainLyrics", out var pv) ? pv.GetString() ?? "" : "";

                int durationPenalty = Math.Min(80, (int)(difference * 8));

                if (p.Length > 0)
                {
                    int score = exactBonus + 1 - durationPenalty;
                    if (score > plainScore) { plainScore = score; plain = p; }
                }

                if (s.Length == 0) continue;
                int sync = exactBonus + 100 - durationPenalty
                           + (WordTagRegex.IsMatch(s) ? (preferEnhanced ? 1000 : 10) : 0);
                if (sync <= syncScore) continue;
                syncScore = sync;
                synced = s;
                if (p.Length > 0) plain ??= p;
            }

            return synced is not null
                ? new Candidate(synced, plain, Txt.LyricsFromLrclib, Txt.LyricsPlain, syncScore)
                : new Candidate(null, plain, "", Txt.LyricsPlain, plainScore);
        }
        catch { return Empty(); }
    }

    /// <summary>Запасной провайдер обычного текста, если lrclib не отвечает.</summary>
    private static async Task<Candidate> FetchLyricsOvhAsync(string title, string artist)
    {
        try
        {
            string url = "https://api.lyrics.ovh/v1/"
                         + Uri.EscapeDataString(artist) + "/" + Uri.EscapeDataString(title);
            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new Candidate(null, null, "", "", int.MinValue);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string plain = json.RootElement.TryGetProperty("lyrics", out var value)
                ? value.GetString() ?? ""
                : "";
            return new Candidate(null, plain, "", "lyrics.ovh · обычный текст", 0);
        }
        catch
        {
            return new Candidate(null, null, "", "", int.MinValue);
        }
    }

    /// <summary>
    /// Genius: ищем трек по API (нужен Bearer-токен), затем читаем текст со страницы.
    /// Возвращает обычный текст — он идет только в резерв, синхронный LRCLib выигрывает.
    /// </summary>
    private static async Task<Candidate> FetchGeniusAsync(string title, string? artist, string token)
    {
        try
        {
            string query = string.IsNullOrWhiteSpace(artist) ? title : artist + " " + title;
            using var search = new HttpRequestMessage(HttpMethod.Get,
                "https://api.genius.com/search?q=" + Uri.EscapeDataString(query));
            search.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var searchResponse = await Client.SendAsync(search);
            if (!searchResponse.IsSuccessStatusCode) return Empty();

            using var searchJson = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
            var hits = searchJson.RootElement.GetProperty("response").GetProperty("hits");
            string? url = null;
            foreach (var hit in hits.EnumerateArray())
            {
                if (hit.ValueKind != JsonValueKind.Object || !hit.TryGetProperty("result", out var result)) continue;
                url = result.TryGetProperty("url", out var value) ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(url)) break;
            }
            if (string.IsNullOrWhiteSpace(url)) return Empty();

            using var page = await Client.GetAsync(url);
            if (!page.IsSuccessStatusCode) return Empty();
            string plain = ExtractGeniusLyrics(await page.Content.ReadAsStringAsync());
            return string.IsNullOrWhiteSpace(plain)
                ? Empty()
                : new Candidate(null, plain, "", "Genius · обычный текст", -5);
        }
        catch { return Empty(); }
    }

    private static string ExtractGeniusLyrics(string html)
    {
        var containers = Regex.Matches(html,
            "<div[^>]*(?:data-lyrics-container=\"true\"|class=\"[^\"]*lyrics[^\"]*\")[^>]*>",
            RegexOptions.IgnoreCase);
        var builder = new StringBuilder();

        foreach (Match container in containers)
        {
            int start = container.Index + container.Length;
            int pos = start;
            int depth = 1;
            while (pos < html.Length && depth > 0)
            {
                int open = html.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
                int close = html.IndexOf("</div>", pos, StringComparison.OrdinalIgnoreCase);
                if (close < 0) break;
                if (open >= 0 && open < close)
                {
                    depth++;
                    pos = open + 4;
                }
                else
                {
                    depth--;
                    pos = close + 6;
                    if (depth == 0) builder.AppendLine(html[start..close]);
                }
            }
        }

        string text = Regex.Replace(builder.ToString(), "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return Regex.Replace(text, "\\n{3,}", "\n\n").Trim();
    }

    /// <summary>Musixmatch: опциональный токеновый источник обычного текста.</summary>
    private static async Task<Candidate> FetchMusixmatchAsync(string title, string? artist, string token)
    {
        try
        {
            string url = "https://api.musixmatch.com/ws/1.1/matcher.lyrics.get?q_track="
                         + Uri.EscapeDataString(title)
                         + "&q_artist=" + Uri.EscapeDataString(artist ?? "")
                         + "&apikey=" + Uri.EscapeDataString(token);
            using var response = await Client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return Empty();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            if (!root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("body", out var body)
                || !body.TryGetProperty("lyrics", out var lyrics)) return Empty();

            string plain = lyrics.TryGetProperty("lyrics_body", out var value) ? value.GetString() ?? "" : "";
            int disclaimer = plain.IndexOf("*******", StringComparison.Ordinal);
            if (disclaimer > 0) plain = plain[..disclaimer];
            return string.IsNullOrWhiteSpace(plain)
                ? Empty()
                : new Candidate(null, plain.Trim(), "", "Musixmatch · обычный текст", -10);
        }
        catch { return Empty(); }
    }

    private static LyricsDocument? TryLoadLocalLrc(TrackItem track)
    {
        var candidates = new List<string>();
        // Явно прикреплённый файл имеет приоритет над соседним .lrc.
        if (!string.IsNullOrWhiteSpace(track.LyricsFile)
            && track.LyricsFile.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
            candidates.Add(track.LyricsFile);
        if (!string.IsNullOrWhiteSpace(track.FileName))
            candidates.Add(Path.ChangeExtension(track.FileName, ".lrc"));

        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path)) continue;
                string text = File.ReadAllText(path);
                var document = Parse(text);
                if (document is not null)
                {
                    document.Source = Txt.LyricsFromLrc;
                    return document;
                }
                var plain = ParsePlain(text);
                if (plain is not null)
                {
                    plain.Source = Txt.LyricsFromLrc;
                    return plain;
                }
            }
            catch { }
        }
        return null;
    }

    private static LyricsDocument? TryLoadEmbedded(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var file = global::TagLib.File.Create(path);
            string text = file.Tag.Lyrics;
            if (string.IsNullOrWhiteSpace(text)) return null;

            var document = Parse(text);
            if (document is not null)
            {
                document.Source = Txt.LyricsFromEmbedded;
                return document;
            }
            var plain = ParsePlain(text);
            if (plain is not null) plain.Source = Txt.LyricsFromEmbedded;
            return plain;
        }
        catch
        {
            return null;
        }
    }

    private static void Cache(string cacheFile, LyricsDocument document)
    {
        if (!SettingsService.Current.SaveLyricsCache) return;
        try
        {
            string? dir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(cacheFile,
                JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static LyricsDocument? Parse(string lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return null;

        var document = new LyricsDocument { HasTimestamps = true };
        var rawLines = lrc.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < rawLines.Length; i++)
        {
            var match = LineRegex.Match(rawLines[i].Trim());
            if (!match.Success) continue;

            double start = ParseTime(match.Groups[1].Value, match.Groups[2].Value);
            string body = match.Groups[3].Value.Trim();
            if (body.Length == 0) continue;

            var words = ParseWords(body, out bool hasWordSync);
            double end = i + 1 < rawLines.Length ? NextLineStart(rawLines, i + 1) : start + 4;
            if (end <= start) end = start + 4;
            if (hasWordSync)
            {
                double last = words.Count > 0 ? words[^1].Start : start;
                end = Math.Max(end, last + 0.8);
            }

            var line = new LyricsLine { Start = start, End = end, RawText = StripTags(body) };
            if (hasWordSync)
            {
                for (int w = 0; w < words.Count; w++)
                {
                    words[w].End = w + 1 < words.Count ? words[w + 1].Start : end;
                    line.Words.Add(words[w]);
                }
            }
            else
            {
                DistributeWords(line, end);
            }
            document.Lines.Add(line);
        }

        document.Lines.Sort((a, b) => a.Start.CompareTo(b.Start));
        return document.Lines.Count > 0 ? document : null;
    }

    private static LyricsDocument? ParsePlain(string? plain, double durationSeconds = 0)
    {
        if (string.IsNullOrWhiteSpace(plain)) return null;
        var document = new LyricsDocument { HasTimestamps = false };
        foreach (var line in plain.Replace("\r\n", "\n").Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0) continue;
            document.Lines.Add(new LyricsLine { RawText = text, Start = 0, End = 0 });
        }
        if (document.Lines.Count == 0) return null;
        // Без длительности не выдумываем метки: слова останутся тусклыми,
        // а не «все сразу белые», как при Start=0/End=0.3.
        return durationSeconds > 0 ? EnsureTiming(document, durationSeconds) : document;
    }

    /// <summary>
    /// Обычный текст без меток распределяем по длительности трека, чтобы караоке,
    /// подсветка активной строки и клик по слову работали и у LRCLib plain-ответов.
    /// </summary>
    public static LyricsDocument EnsureTiming(LyricsDocument document, double durationSeconds)
    {
        if (document.HasTimestamps || durationSeconds <= 0) return document;
        var lines = document.Lines.Where(l => !string.IsNullOrWhiteSpace(l.RawText)).ToList();
        if (lines.Count == 0) return document;

        document.HasTimestamps = true;
        double lead = Math.Min(2.0, durationSeconds * 0.05);
        double usable = Math.Max(1.0, durationSeconds - lead - 1.0);
        for (int i = 0; i < lines.Count; i++)
        {
            double start = lead + usable * i / lines.Count;
            double end = lead + usable * (i + 1) / lines.Count;
            lines[i].Start = start;
            lines[i].End = end;
            lines[i].Words.Clear();
            DistributeWords(lines[i], end);
        }
        return document;
    }

    private static double NextLineStart(string[] lines, int from)
    {
        for (int i = from; i < lines.Length; i++)
        {
            var m = LineRegex.Match(lines[i].Trim());
            if (m.Success) return ParseTime(m.Groups[1].Value, m.Groups[2].Value);
        }
        return 0;
    }

    private static List<LyricsWord> ParseWords(string body, out bool hasWordSync)
    {
        var result = new List<LyricsWord>();
        hasWordSync = WordTagRegex.IsMatch(body);
        if (!hasWordSync) return result;

        var matches = WordTagRegex.Matches(body);
        for (int i = 0; i < matches.Count; i++)
        {
            int textStart = matches[i].Index + matches[i].Length;
            int textEnd = i + 1 < matches.Count ? matches[i + 1].Index : body.Length;
            string text = body.Substring(textStart, textEnd - textStart).Trim();
            if (text.Length == 0) continue;
            result.Add(new LyricsWord
            {
                Text = text,
                Start = ParseTime(matches[i].Groups[1].Value, matches[i].Groups[2].Value)
            });
        }
        return result;
    }

    /// <summary>
    /// Если у строки нет пословных меток, распределяем слова по длительности так,
    /// чтобы между ними оставались короткие паузы, а знаки препинания чуть удлиняли слово.
    /// Это делает побуквенную подсветку похожей на пение, а не на равномерную заливку.
    /// </summary>
    private static void DistributeWords(LyricsLine line, double end)
    {
        string[] parts = line.RawText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        double span = Math.Max(0.3, end - line.Start);
        double gaps = Math.Min(span * 0.18, 0.07 * (parts.Length - 1));
        double sounding = Math.Max(0.12, span - gaps);
        double weightSum = parts.Sum(p => WordWeight(p));
        double cursor = line.Start;

        foreach (var part in parts)
        {
            double length = sounding * WordWeight(part) / weightSum;
            line.Words.Add(new LyricsWord { Text = part, Start = cursor, End = cursor + length });
            cursor += length + (parts.Length > 1 ? gaps / (parts.Length - 1) : 0);
        }
    }

    private static double WordWeight(string word)
    {
        double weight = Math.Max(1, word.Length);
        char last = word[^1];
        if (last is ',' or ';' or ':' or '—' or '-') weight += 0.6;
        if (last is '.' or '!' or '?') weight += 1.2;
        return weight;
    }

    private static string StripTags(string body) => WordTagRegex.Replace(body, "").Trim();

    private static double ParseTime(string minutes, string seconds)
    {
        double m = double.Parse(minutes, CultureInfo.InvariantCulture);
        double s = double.Parse(seconds.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m * 60 + s;
    }
}
