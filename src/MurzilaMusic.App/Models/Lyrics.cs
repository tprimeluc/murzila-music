namespace MurzilaMusic.App.Models;

public sealed class LyricsWord
{
    public string Text { get; set; } = "";
    public double Start { get; set; }
    public double End { get; set; }
}

public sealed class LyricsLine
{
    public double Start { get; set; }
    public double End { get; set; }
    public string RawText { get; set; } = "";
    public List<LyricsWord> Words { get; set; } = new();
}

public sealed class LyricsDocument
{
    public List<LyricsLine> Lines { get; set; } = new();
    public bool HasTimestamps { get; set; }
    public string Source { get; set; } = "lrclib";

    /// <summary>Строка, которая звучит в указанный момент (для строки в плеере и видео).</summary>
    public LyricsLine? LineAt(double seconds)
    {
        LyricsLine? found = null;
        foreach (var line in Lines)
        {
            if (line.Start > seconds) break;
            // Строка, которая уже закончилась, больше не считается звучащей.
            if (line.End <= 0 || line.End >= seconds) found = line;
        }
        return found;
    }
}

public sealed class AccessDecision
{
    public bool Allowed { get; set; }
    public bool NeedsLogin { get; set; }
    public bool NeedsPayment { get; set; }
    public int AmountRub { get; set; }
    public string Reason { get; set; } = "";
    public Tariff RequiredTariff { get; set; } = Tariff.Free;
}
