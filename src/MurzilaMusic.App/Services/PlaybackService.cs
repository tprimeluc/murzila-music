using NAudio.Dsp;
using NAudio.Vorbis;
using NAudio.Wave;
using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Прозрачный слой над потоком аудио: собирает моно-сэмплы для визуализатора,
/// не меняя звук.
/// </summary>
public sealed class SpectrumTap : ISampleProvider
{
    private readonly ISampleProvider _source;
    private const int RingSize = 8192;
    private readonly float[] _ring = new float[RingSize];
    private readonly object _ringLock = new();
    private int _position;
    private int _written;

    public SpectrumTap(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int channels = Math.Max(1, WaveFormat.Channels);

        lock (_ringLock)
        {
            for (int i = 0; i < read; i += channels)
            {
                float sum = 0;
                int n = Math.Min(channels, read - i);
                for (int c = 0; c < n; c++) sum += buffer[offset + i + c];
                _ring[_position] = sum / n;
                _position = (_position + 1) % RingSize;
                _written++;
            }
        }
        return read;
    }

    /// <summary>Заполняет массив полос (0..1) спектром последнего фрагмента звука.</summary>
    public void GetBands(float[] bands)
    {
        const int fftSize = 2048;
        if (bands.Length == 0) return;
        Array.Clear(bands);

        var complex = new Complex[fftSize];
        lock (_ringLock)
        {
            // Пока буфер не наполнился, не показываем «мусорный» спектр.
            if (_written < fftSize / 2) return;
            for (int i = 0; i < fftSize; i++)
            {
                int index = (_position - fftSize + i + RingSize * 2) % RingSize;
                double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
                complex[i].X = (float)(_ring[index] * window);
                complex[i].Y = 0;
            }
        }
        FastFourierTransform.FFT(true, 11, complex);

        int useful = fftSize / 2;
        for (int b = 0; b < bands.Length; b++)
        {
            double from = Math.Pow((double)b / bands.Length, 2.0) * useful;
            double to = Math.Pow((double)(b + 1) / bands.Length, 2.0) * useful;
            int start = (int)from;
            int end = Math.Max(start + 1, (int)to);

            double peak = 0;
            for (int i = start; i < end && i < useful; i++)
                peak = Math.Max(peak, Math.Sqrt(complex[i].X * complex[i].X + complex[i].Y * complex[i].Y));

            double value = Math.Min(1, Math.Log10(1 + peak * 60) / 2.6);
            bands[b] = (float)Math.Max(0, value * (0.65 + 0.35 * (double)b / bands.Length));
        }
    }
}

public sealed class PlaybackService : IDisposable
{
    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private EqualizerSampleProvider? _equalizer;
    private SpectrumTap? _tap;
    private TrackItem? _current;

    public event EventHandler? PlaybackEnded;
    public event EventHandler? StateChanged;

    public TrackItem? Current => _current;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
    public double PositionSeconds => _reader?.CurrentTime.TotalSeconds ?? 0;
    public double DurationSeconds => _reader?.TotalTime.TotalSeconds ?? _current?.DurationSeconds ?? 0;

    public double Volume
    {
        get => _output?.Volume ?? 0.75;
        set
        {
            if (_output is not null) _output.Volume = Math.Clamp((float)value, 0, 1);
        }
    }

    public void Play(TrackItem track)
    {
        Stop();
        _current = track;

        try
        {
            _reader = CreateReader(track.FileName);
            var sampleProvider = _reader.ToSampleProvider();
            _equalizer = new EqualizerSampleProvider(sampleProvider);
            _equalizer.Update(SettingsService.Current.EqualizerBands, SettingsService.Current.EqualizerEnabled);
            _tap = new SpectrumTap(_equalizer);
            // Устройство могло отключиться (Bluetooth) — не даём WaveOutEvent упасть.
            int device = Math.Clamp(SettingsService.Current.OutputDeviceNumber, 0,
                Math.Max(0, WaveOut.DeviceCount - 1));
            _output = new WaveOutEvent { DeviceNumber = device };
            _output.Init(_tap);
            _output.Volume = (float)Math.Clamp(SettingsService.Current.Volume, 0, 1);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    /// <summary>MP3/WAV/AIFF — напрямую, OGG — через NVorbis, остальное (FLAC/M4A/MP4/AAC/WMA) — Media Foundation.</summary>
    private static WaveStream CreateReader(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => new WaveFileReader(path),
            ".mp3" => new Mp3FileReader(path),
            ".aiff" or ".aif" => new AiffFileReader(path),
            ".ogg" => new VorbisWaveReader(path),
            _ => new MediaFoundationReader(path)
        };
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null && _reader is not null && _reader.Position >= _reader.Length - 1000)
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TogglePause()
    {
        if (_output is null) return;
        if (_output.PlaybackState == PlaybackState.Playing) _output.Pause();
        else _output.Play();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        if (_output?.PlaybackState == PlaybackState.Playing) _output.Pause();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (_output?.PlaybackState == PlaybackState.Paused) _output.Play();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Применяет текущие настройки эквалайзера без перезапуска трека.</summary>
    public void ApplyEqualizer() =>
        _equalizer?.Update(SettingsService.Current.EqualizerBands, SettingsService.Current.EqualizerEnabled);

    public void Stop()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }
        _tap = null;
        _equalizer = null;
        _reader?.Dispose();
        _reader = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(double seconds)
    {
        if (_reader is null) return;
        var target = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, Math.Max(0, DurationSeconds - 0.2)));
        _reader.CurrentTime = target;
    }

    public void GetBands(float[] bands) => _tap?.GetBands(bands);

    public void Dispose() => Stop();
}
