using NAudio.Dsp;
using NAudio.Wave;

namespace MurzilaMusic.App.Services;

/// <summary>Пресеты 10-полосного эквалайзера. Значения в дБ.</summary>
public static class EqualizerPresets
{
    public static readonly double[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static readonly (string Name, double[] Gains)[] All =
    {
        ("Ровно", new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
        ("Бас", new double[] { 7, 6, 4, 2, 0, 0, 0, 0, 0, -1 }),
        ("Вокал", new double[] { -2, -1, 0, 1, 3, 5, 5, 3, 1, 0 }),
        ("Рок", new double[] { 4, 4, 2, -1, 0, 2, 3, 4, 4, 3 }),
        ("Поп", new double[] { -1, 0, 2, 3, 3, 4, 4, 2, 1, 0 }),
        ("Электро", new double[] { 5, 5, 2, -1, 0, 2, 4, 5, 5, 4 })
    };

    public static List<double> Flat() => new(All[0].Gains);

    public static List<double> Get(string name)
    {
        foreach (var preset in All)
            if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                return new List<double>(preset.Gains);
        return Flat();
    }
}

/// <summary>
/// 10-полосный параметрический эквалайзер на биквадах. Работает поверх
/// уже декодированного звука и не меняет формат / громкость сверх заданных полос.
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile BiQuadFilter[][]? _chains;

    public EqualizerSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void Update(IReadOnlyList<double> gains, bool enabled)
    {
        if (!enabled)
        {
            _chains = null;
            return;
        }

        int rate = WaveFormat.SampleRate;
        int channels = Math.Max(1, WaveFormat.Channels);
        int bands = Math.Min(EqualizerPresets.Frequencies.Length, gains.Count);
        var chains = new BiQuadFilter[channels][];
        bool anything = false;

        for (int ch = 0; ch < channels; ch++)
        {
            var filters = new List<BiQuadFilter>(bands);
            for (int b = 0; b < bands; b++)
            {
                double gain = Math.Clamp(gains[b], -12, 12);
                if (Math.Abs(gain) < 0.05) continue;

                double frequency = EqualizerPresets.Frequencies[b];
                if (frequency >= rate / 2.0 - 100) continue;

                filters.Add(BiQuadFilter.PeakingEQ(rate, (float)frequency, 1.0f, (float)gain));
                anything = true;
            }
            chains[ch] = filters.ToArray();
        }

        _chains = anything ? chains : null;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var chains = _chains;
        if (chains is null || read <= 0) return read;

        int channels = Math.Max(1, WaveFormat.Channels);
        for (int i = 0; i < read; i++)
        {
            int channel = i % channels;
            var chain = chains[channel];
            if (chain.Length == 0) continue;

            float sample = buffer[offset + i];
            for (int f = 0; f < chain.Length; f++)
                sample = chain[f].Transform(sample);
            buffer[offset + i] = sample;
        }
        return read;
    }
}
