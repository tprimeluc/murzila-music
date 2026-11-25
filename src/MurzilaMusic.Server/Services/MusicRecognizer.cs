using System.Diagnostics;
using System.Globalization;

namespace MurzilaMusic.Server;

/// <summary>
/// Офлайн-распознавание по своим трекам: спектральный «отпечаток» PCM + сравнение со сдвигом по времени.
/// Никаких внешних каталогов:matched track берётся только из библиотеки пользователя.
/// </summary>
public static class MusicRecognizer
{
    public const int SampleRate = 16000;
    public const int Bands = 32;
    private const int WindowMs = 100;

    public static float[][] Fingerprint(short[] pcm, int sampleRate)
    {
        int window = Math.Max(64, sampleRate / (1000 / WindowMs));
        int frames = Math.Max(0, pcm.Length / window - 1);
        var result = new float[frames][];
        var freqs = new double[Bands];
        for (int b = 0; b < Bands; b++)
            freqs[b] = 80 * Math.Pow(9000.0 / 80, b / (double)(Bands - 1));

        for (int f = 0; f < frames; f++)
        {
            var row = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                double freq = freqs[b];
                double coeff = 2 * Math.Cos(2 * Math.PI * freq / sampleRate);
                double s1 = 0, s2 = 0;
                int start = f * window;
                for (int i = 0; i < window; i++)
                {
                    double x = pcm[start + i] / 32768.0;
                    double s0 = x + coeff * s1 - s2;
                    s2 = s1;
                    s1 = s0;
                }
                row[b] = (float)Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - coeff * s1 * s2));
            }
            float max = row.Max();
            if (max > 0.00001f)
                for (int b = 0; b < Bands; b++) row[b] /= max;
            result[f] = row;
        }
        return result;
    }

    public static double Score(float[][] mic, float[][] track)
    {
        if (mic.Length == 0 || track.Length == 0) return 0;
        double best = 0;
        for (int shift = -20; shift <= 20; shift++)
        {
            double sum = 0; int n = 0;
            for (int i = 0; i < mic.Length; i++)
            {
                int j = i + shift;
                if (j < 0 || j >= track.Length) continue;
                float dot = 0, a = 0, b = 0;
                for (int k = 0; k < Bands; k++)
                {
                    dot += mic[i][k] * track[j][k];
                    a += mic[i][k] * mic[i][k];
                    b += track[j][k] * track[j][k];
                }
                if (a > 0 && b > 0) { sum += dot / Math.Sqrt(a * b); n++; }
            }
            if (n > 5) best = Math.Max(best, sum / n);
        }
        return best;
    }

    public static byte[] ToWav(short[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int data = pcm.Length * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + data);
        w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16);
        w.Write((short)1); w.Write((short)1); w.Write(sampleRate); w.Write(sampleRate * 2);
        w.Write((short)2); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(data);
        foreach (short s in pcm) w.Write(s);
        return ms.ToArray();
    }

    public static async Task<short[]?> DecodeAsync(string path)
    {
        string? ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return null;
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                ArgumentList =
                {
                    "-hide_banner", "-loglevel", "error", "-i", path,
                    "-ac", "1", "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                    "-t", "30", "-f", "s16le", "pipe:1"
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var ms = new MemoryStream();
            await p.StandardOutput.BaseStream.CopyToAsync(ms);
            await p.WaitForExitAsync();
            byte[] bytes = ms.ToArray();
            var pcm = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);
            return pcm.Length > SampleRate ? pcm : null;
        }
        catch { return null; }
    }

    private static string? FindFfmpeg()
    {
        string? fromPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"))
            .FirstOrDefault(File.Exists);
        if (fromPath is not null) return fromPath;
        string local = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        return File.Exists(local) ? local : null;
    }
}
