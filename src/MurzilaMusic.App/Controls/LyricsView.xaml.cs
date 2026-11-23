using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App.Controls;

/// <summary>
/// Синхронный текст в стиле Spotify: каждая буква плавно белеет, чуть поднимается
/// и увеличивается строго в момент своего звучания. Промежуточные состояния
/// интерполируются 60 раз в секунду, поэтому движение остаётся плавным даже при
/// том, что плеер отдаёт позицию раз в 60 мс.
/// </summary>
public partial class LyricsView : UserControl
{
    /// <summary>Нажатие на строку/слово/букву просит плеер перемотать к этому моменту.</summary>
    public event EventHandler<double>? SeekRequested;

    public bool AllowSeek { get; set; } = true;
    public bool AnimateKaraoke { get; set; } = true;
    public bool ShowGlow { get; set; } = true;
    public double GlyphFontSize { get; set; } = 25;

    private sealed class Glyph
    {
        public TextBlock Block = null!;
        public SolidColorBrush Brush = null!;
        public TranslateTransform Move = null!;
        public ScaleTransform Scale = null!;
        public double Start;
        public double End;
        public double Target;
        public double Value;
        public bool Applied;
    }

    private sealed class Row
    {
        public LyricsLine Line = new();
        public WrapPanel Panel = new();
        public List<Glyph> Glyphs = new();
        public TextBlock? Plain;
        public DropShadowEffect? Glow;
    }

    private static readonly Color DimActive = Color.FromRgb(150, 150, 166);
    private static readonly Color DimOther = Color.FromRgb(88, 88, 102);
    private static readonly Color Bright = Colors.White;

    private readonly List<Row> _rows = new();
    private readonly DispatcherTimer _animation;
    private LyricsDocument? _document;
    private int _activeIndex = -1;
    private string _emptyMessage = "Текст не найден";
    private double _time;
    private double _lastTime;
    private bool _snap;
    private double _scrollTarget;
    private double _scrollCurrent;
    private long _lastFrame;

    public LyricsView()
    {
        InitializeComponent();

        _animation = new DispatcherTimer(TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render, Animate, Dispatcher);
        Loaded += (_, _) => { if (!_animation.IsEnabled) _animation.Start(); };
        Unloaded += (_, _) => _animation.Stop();
        _animation.Start();
    }

    /// <summary>Применить настройки караоке и пересобрать текст с новым размером букв.</summary>
    public void ApplySettings()
    {
        var s = SettingsService.Current;
        AllowSeek = s.LyricsClickToSeek;
        AnimateKaraoke = s.KaraokeAnimation;
        ShowGlow = s.LyricsGlow;
        GlyphFontSize = Math.Clamp(s.LyricsFontSize, 16, 40);
        if (_document is not null) SetDocument(_document, _emptyMessage);
    }

    public void SetDocument(LyricsDocument? document, string? emptyMessage = null)
    {
        _document = document;
        _rows.Clear();
        Lines.Children.Clear();
        _activeIndex = -1;
        _time = 0;
        _lastTime = 0;
        _snap = true;
        _scrollTarget = 0;
        _scrollCurrent = 0;
        Scroll.ScrollToVerticalOffset(0);
        if (!string.IsNullOrWhiteSpace(emptyMessage)) _emptyMessage = emptyMessage;

        if (document is null)
        {
            Lines.Children.Add(new TextBlock
            {
                Text = _emptyMessage,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 132)),
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            });
            return;
        }

        foreach (var line in document.Lines)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            var row = new Row { Line = line, Panel = panel };
            bool clickable = AllowSeek && document.HasTimestamps && line.Start >= 0;
            if (clickable)
            {
                panel.Tag = (row, Math.Max(0, line.Start));
                panel.Cursor = Cursors.Hand;
                panel.MouseLeftButtonUp += Lyrics_Click;
                panel.ToolTip = Txt.LyricsSeekTip;
            }

            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(190, 225, 255),
                ShadowDepth = 0,
                BlurRadius = 14,
                Opacity = 0
            };
            if (ShowGlow) panel.Effect = glow;
            row.Glow = ShowGlow ? glow : null;

            if (line.Words.Count == 0)
            {
                var plain = new TextBlock
                {
                    Text = line.RawText,
                    Foreground = new SolidColorBrush(DimOther),
                    FontSize = GlyphFontSize - 1,
                    FontWeight = FontWeights.SemiBold
                };
                if (clickable)
                {
                    plain.Tag = (row, Math.Max(0, line.Start));
                    plain.Cursor = Cursors.Hand;
                    plain.MouseLeftButtonUp += Lyrics_Click;
                }
                panel.Children.Add(plain);
                row.Plain = plain;
            }
            else
            {
                foreach (var word in line.Words)
                    AddWord(row, word);
            }

            _rows.Add(row);
            Lines.Children.Add(panel);
        }

        Update(0);
    }

    private void AddWord(Row row, LyricsWord word)
    {
        // Слово не разрывается: переносы происходят только между словами.
        var wordPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        string text = word.Text;
        double span = Math.Max(0.12, word.End - word.Start);
        int count = Math.Max(1, text.Length);

        // Гласные звучат дольше согласных: буква «поёт» вместе с вокалом,
        // а не просто получает равную долю слова.
        var weights = new double[count];
        double totalWeight = 0;
        for (int i = 0; i < count; i++)
        {
            weights[i] = GlyphWeight(text[i]);
            totalWeight += weights[i];
        }

        double cursor = word.Start;
        for (int i = 0; i < count; i++)
        {
            double length = span * weights[i] / totalWeight;
            double from = cursor;
            double to = cursor + length;
            cursor = to;

            var brush = new SolidColorBrush(DimOther);
            var move = new TranslateTransform(0, 0);
            var scale = new ScaleTransform(1, 1);
            var transform = new TransformGroup();
            transform.Children.Add(scale);
            transform.Children.Add(move);

            var block = new TextBlock
            {
                Text = text[i].ToString(),
                Foreground = brush,
                FontSize = GlyphFontSize,
                FontWeight = FontWeights.SemiBold,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            if (AllowSeek && _document?.HasTimestamps == true)
            {
                block.Tag = (row, Math.Max(0, from));
                block.Cursor = Cursors.Hand;
                block.MouseLeftButtonUp += Lyrics_Click;
            }

            wordPanel.Children.Add(block);
            row.Glyphs.Add(new Glyph
            {
                Block = block,
                Brush = brush,
                Move = move,
                Scale = scale,
                Start = from,
                End = to
            });
        }

        row.Panel.Children.Add(wordPanel);
    }

    private void Lyrics_Click(object sender, MouseButtonEventArgs e)
    {
        if (!AllowSeek || _document?.HasTimestamps != true) return;
        if (sender is FrameworkElement element && element.Tag is (Row _, double time))
        {
            e.Handled = true;
            SeekRequested?.Invoke(this, Math.Max(0, time - 0.12));
        }
    }

    private static double GlyphWeight(char c)
    {
        if (char.IsWhiteSpace(c)) return 0.25;
        if (char.IsDigit(c)) return 0.8;
        if (!char.IsLetter(c)) return 0.35;

        char lower = char.ToLowerInvariant(c);
        if ("аеёиоуыэюяaeiouy".IndexOf(lower) >= 0) return 1.5;
        if ("лмнрй".IndexOf(lower) >= 0) return 1.2; // сонорные тоже тянутся
        return 0.9;
    }

    /// <summary>Плеер обновляет позицию раз в 60 мс — здесь только цель для анимации.</summary>
    public void Update(double timeSeconds)
    {
        if (_document is null) return;

        if (Math.Abs(timeSeconds - _lastTime) > 0.6) _snap = true; // перемотка
        _time = Math.Max(0, timeSeconds);
        _lastTime = _time;

        int active = -1;
        if (_document.HasTimestamps)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var line = _rows[i].Line;
                // После окончания строки подсветка гаснет: прошедший текст не остаётся белым.
                if (line.Start <= _time && (line.End <= 0 || line.End >= _time)) active = i;
            }
        }

        if (active != _activeIndex)
        {
            _activeIndex = active;
            // Подсветка соседних строк зависит от активной — перекрашиваем все.
            foreach (var row in _rows)
                foreach (var glyph in row.Glyphs) glyph.Applied = false;
            if (active >= 0 && active < _rows.Count) SetScrollTarget(active);
        }

        foreach (var row in _rows)
        {
            bool timed = _document.HasTimestamps;
            bool future = timed && row.Line.Start > _time + 0.02;
            bool past = timed && row.Line.End > 0 && row.Line.End < _time - 0.05;
            foreach (var glyph in row.Glyphs)
            {
                double target = 0;
                if (timed && !future && !past)
                {
                    double raw = (timeSeconds - glyph.Start) / Math.Max(0.02, glyph.End - glyph.Start);
                    raw = Math.Clamp(raw, 0, 1);
                    target = raw * raw * (3 - 2 * raw); // плавный разгон и торможение
                }
                glyph.Target = target;
            }
        }

        if (_snap)
        {
            for (int i = 0; i < _rows.Count; i++)
                foreach (var glyph in _rows[i].Glyphs)
                {
                    glyph.Value = glyph.Target;
                    Apply(glyph, i == _activeIndex);
                }
            _snap = false;
        }
    }

    private void Animate(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = _lastFrame == 0 ? 0.016 : Math.Min(0.05, (now - _lastFrame) / 1000.0);
        _lastFrame = now;

        // Разные постоянные времени: быстрое загорание, мягкое затухание,
        // очень плавная прокрутка к поющейся строке.
        double rise = 1 - Math.Exp(-dt / 0.045);
        double fall = 1 - Math.Exp(-dt / 0.20);

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool active = i == _activeIndex;
            foreach (var glyph in row.Glyphs)
            {
                double target = glyph.Target;
                if (!AnimateKaraoke)
                {
                    if (glyph.Value != target || !glyph.Applied)
                    {
                        glyph.Value = target;
                        Apply(glyph, active);
                    }
                    continue;
                }
                double k = target > glyph.Value ? rise : fall;
                double next = Math.Abs(target - glyph.Value) < 0.0004
                    ? target
                    : glyph.Value + (target - glyph.Value) * k;
                if (glyph.Applied && Math.Abs(next - glyph.Value) < 0.0002) continue;

                glyph.Value = next;
                Apply(glyph, active);
            }

            if (row.Glow is not null)
            {
                double wanted = active && row.Glyphs.Count > 0 ? 0.28 : 0;
                row.Glow.Opacity += (wanted - row.Glow.Opacity) * (1 - Math.Exp(-dt / 0.25));
            }

            if (row.Plain is not null)
            {
                var target = active ? DimActive : DimOther;
                var brush = (SolidColorBrush)row.Plain.Foreground;
                brush.Color = Color.FromRgb(
                    (byte)(brush.Color.R + (target.R - brush.Color.R) * rise),
                    (byte)(brush.Color.G + (target.G - brush.Color.G) * rise),
                    (byte)(brush.Color.B + (target.B - brush.Color.B) * rise));
            }
        }

        double wantedScroll = Math.Clamp(_scrollTarget, 0, Math.Max(0, Scroll.ScrollableHeight));
        if (Math.Abs(_scrollCurrent - wantedScroll) > 0.2)
        {
            _scrollCurrent += (wantedScroll - _scrollCurrent) * (1 - Math.Exp(-dt / 0.22));
            Scroll.ScrollToVerticalOffset(_scrollCurrent);
        }
    }

    private static void Apply(Glyph glyph, bool isActiveLine)
    {
        double v = Math.Clamp(glyph.Value, 0, 1);
        Color dim = isActiveLine ? DimActive : DimOther;
        glyph.Brush.Color = Color.FromRgb(
            (byte)(dim.R + (Bright.R - dim.R) * v),
            (byte)(dim.G + (Bright.G - dim.G) * v),
            (byte)(dim.B + (Bright.B - dim.B) * v));

        glyph.Move.Y = -6 * v;
        double size = 1 + 0.045 * v;
        glyph.Scale.ScaleX = size;
        glyph.Scale.ScaleY = size;
        glyph.Block.Opacity = 0.72 + 0.28 * v;
        glyph.Applied = true;
    }

    private void SetScrollTarget(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (index >= _rows.Count) return;
            var point = _rows[index].Panel.TransformToVisual(Lines).Transform(new Point(0, 0));
            _scrollTarget = Math.Max(0, point.Y - Scroll.ViewportHeight / 2 + 70);
        }));
    }
}
