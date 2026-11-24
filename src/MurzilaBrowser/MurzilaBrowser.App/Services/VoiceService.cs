using System.Speech.Synthesis;

namespace MurzilaBrowser.App.Services;

/// <summary>
/// Обычные голоса: системный TTS переводит текст и сразу говорит.
/// Живые голоса: тот же текст разбивается на реплики и озвучивается
/// разными системными голосами — результат звучит как диалог и занимает минуты.
/// </summary>
public sealed class VoiceService
{
    public IReadOnlyList<string> Voices
    {
        get
        {
            using var synth = new SpeechSynthesizer();
            return synth.GetInstalledVoices().Select(v => v.VoiceInfo.Name).ToList();
        }
    }

    public void Speak(string text, string lang = "ru", string? voice = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        using var synth = new SpeechSynthesizer();
        synth.Rate = 1;
        if (!string.IsNullOrWhiteSpace(voice) && Voices.Contains(voice)) synth.SelectVoice(voice);
        else
        {
            var target = synth.GetInstalledVoices().FirstOrDefault(v =>
                v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(lang, StringComparison.OrdinalIgnoreCase));
            if (target != null) synth.SelectVoice(target.VoiceInfo.Name);
        }
        synth.Speak(text);
    }

    public async Task DubAsync(string text, string lang, IProgress<(int Done, int Total)>? progress = null, CancellationToken token = default)
    {
        var lines = text.Split(new[] { '\n', '.', '!' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 2).Take(200).ToList();
        if (lines.Count == 0) return;
        var names = Voices;
        for (int i = 0; i < lines.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            Speak(lines[i], lang, names.Count > 0 ? names[i % names.Count] : null);
            progress?.Report((i + 1, lines.Count));
            await Task.Delay(60, token);
        }
    }
}
