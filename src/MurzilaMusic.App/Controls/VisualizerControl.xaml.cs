using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MurzilaMusic.App.Controls;

/// <summary>
/// Визуализатор как на референсе: 24 широких разноцветных столбика
/// от общей линии, с затухающим отражением внизу. Цвета сняты с картинки.
/// </summary>
public partial class VisualizerControl : UserControl
{
    private const int BarCount = 24;

    private readonly float[] _target = new float[BarCount];
    private readonly float[] _display = new float[BarCount];
    private readonly float[] _peaks = new float[BarCount];
    private readonly Brush[] _barBrushes = new Brush[BarCount];
    private readonly Brush[] _reflectionBrushes = new Brush[BarCount];
    private readonly Brush[] _glowBrushes = new Brush[BarCount];

    private static readonly Color[] Palette =
    {
        Color.FromRgb(107, 52, 190),
        Color.FromRgb(157, 0, 196),
        Color.FromRgb(229, 0, 255),
        Color.FromRgb(255, 95, 172),
        Color.FromRgb(189, 0, 75),
        Color.FromRgb(255, 43, 43),
        Color.FromRgb(255, 147, 48),
        Color.FromRgb(255, 115, 0),
        Color.FromRgb(253, 253, 28),
        Color.FromRgb(255, 241, 49),
        Color.FromRgb(255, 253, 113),
        Color.FromRgb(229, 254, 5),
        Color.FromRgb(40, 237, 0),
        Color.FromRgb(0, 227, 0),
        Color.FromRgb(40, 237, 0),
        Color.FromRgb(114, 253, 0),
        Color.FromRgb(88, 251, 255),
        Color.FromRgb(0, 204, 255),
        Color.FromRgb(0, 128, 249),
        Color.FromRgb(0, 4, 223),
        Color.FromRgb(0, 0, 121),
        Color.FromRgb(60, 20, 160),
        Color.FromRgb(100, 0, 180),
        Color.FromRgb(80, 0, 120)
    };

    public VisualizerControl()
    {
        InitializeComponent();
        BuildBrushes();
        SizeChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    public void Update(float[] bands)
    {
        if (bands.Length == 0) return;

        float max = 0;
        for (int i = 0; i < bands.Length; i++) max = Math.Max(max, bands[i]);
        float gain = max > 0.004f ? Math.Min(4.5f, 0.95f / max) : 0;

        for (int i = 0; i < BarCount; i++)
        {
            int from = (int)((long)i * bands.Length / BarCount);
            int to = Math.Max(from + 1, (int)((long)(i + 1) * bands.Length / BarCount));
            float sum = 0;
            int count = 0;
            for (int b = from; b < to && b < bands.Length; b++)
            {
                sum += bands[b];
                count++;
            }

            float value = count > 0 ? sum / count : 0;
            _target[i] = Math.Clamp(value * gain, 0, 1);
            _display[i] = _target[i] > _display[i]
                ? _display[i] + (_target[i] - _display[i]) * 0.55f
                : _display[i] * 0.86f + _target[i] * 0.14f;
            _peaks[i] = Math.Max(_display[i], _peaks[i] - 0.012f);
        }
        InvalidateVisual();
    }

    public void Reset()
    {
        for (int i = 0; i < BarCount; i++)
        {
            _display[i] *= 0.72f;
            _peaks[i] *= 0.82f;
            if (_display[i] < 0.01f) _display[i] = 0;
            if (_peaks[i] < 0.01f) _peaks[i] = 0;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 30 || height < 24) return;

        double baseline = height * 0.58;
        double maxUp = Math.Max(2, baseline - 7);
        double maxDown = Math.Max(2, height - baseline - 4);
        double step = width / BarCount;
        double barWidth = Math.Max(3, step * 0.78);

        // Мягкая полоса под линией — как свечение в референсе.
        var baselineGlow = new LinearGradientBrush(
            Color.FromArgb(0, 255, 255, 255),
            Color.FromArgb(28, 255, 255, 255), 90);
        baselineGlow.Freeze();
        dc.DrawRectangle(baselineGlow, null, new Rect(0, baseline - 10, width, 20));

        for (int i = 0; i < BarCount; i++)
        {
            double value = Math.Clamp(_display[i], 0, 1);
            double barHeight = 2 + value * maxUp;
            double x = i * step + (step - barWidth) / 2;

            // Мягкое свечение вокруг столбика.
            dc.DrawRoundedRectangle(
                _glowBrushes[i], null,
                new Rect(x - 3, baseline - barHeight - 3, barWidth + 6, barHeight + 7), 5, 5);

            dc.DrawRoundedRectangle(
                _barBrushes[i], null,
                new Rect(x, baseline - barHeight, barWidth, barHeight), 2.2, 2.2);

            // Пик слегка задерживается сверху — живая индикация.
            double peakY = baseline - (2 + Math.Clamp(_peaks[i], 0, 1) * maxUp) - 4;
            dc.DrawRoundedRectangle(
                _barBrushes[i], null,
                new Rect(x, peakY, barWidth, 3), 1.5, 1.5);

            double reflectionHeight = Math.Min(maxDown, 3 + barHeight * 0.72);
            dc.DrawRoundedRectangle(
                _reflectionBrushes[i], null,
                new Rect(x, baseline + 3, barWidth, reflectionHeight), 2.2, 2.2);
        }

        var lineBrush = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
        lineBrush.Freeze();
        dc.DrawRectangle(lineBrush, null, new Rect(0, baseline - 0.5, width, 1));
    }

    private void BuildBrushes()
    {
        for (int i = 0; i < BarCount; i++)
        {
            Color color = Palette[i];
            var bar = new LinearGradientBrush(
                Lighten(color, 30),
                color,
                new Point(0, 0),
                new Point(0, 1));
            bar.Freeze();
            _barBrushes[i] = bar;

            var glow = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
            glow.Freeze();
            _glowBrushes[i] = glow;

            var reflection = new LinearGradientBrush(
                Color.FromArgb(85, color.R, color.G, color.B),
                Color.FromArgb(0, color.R, color.G, color.B),
                new Point(0, 0),
                new Point(0, 1));
            reflection.Freeze();
            _reflectionBrushes[i] = reflection;
        }
    }

    private static Color Lighten(Color color, byte amount) => Color.FromRgb(
        (byte)Math.Min(255, color.R + amount),
        (byte)Math.Min(255, color.G + amount),
        (byte)Math.Min(255, color.B + amount));
}
