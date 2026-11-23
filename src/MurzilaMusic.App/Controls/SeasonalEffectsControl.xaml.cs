using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App.Controls;

/// <summary>
/// Лёгкий сезонный слой: зима — снег, весна — зелёные листья,
/// лето — листья и тёплое солнце, осень — оранжевые листья.
/// Слой все время прозрачен для кликов.
/// </summary>
public partial class SeasonalEffectsControl : UserControl
{
    private sealed class Particle
    {
        public System.Windows.UIElement Element = null!;
        public RotateTransform Rotation = null!;
        public double X;
        public double Y;
        public double Speed;
        public double Drift;
        public double Spin;
        public double Phase;
    }

    private readonly DispatcherTimer _timer;
    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private double _elapsed;

    public SeasonalEffectsControl()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Tick, Dispatcher);
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Перечитать настройки и пересобрать частицы.</summary>
    public void Refresh()
    {
        _particles.Clear();
        Particles.Children.Clear();
        SunGlow.Visibility = Visibility.Collapsed;

        var settings = SettingsService.Current;
        if (!settings.SeasonalEffects || settings.ParticleDensity <= 0) return;

        string season = ResolveSeason(settings.SeasonMode);
        if (season == "Лето") SunGlow.Visibility = Visibility.Visible;

        for (int i = 0; i < settings.ParticleDensity; i++)
            _particles.Add(CreateParticle(season));
    }

    private static string ResolveSeason(string mode) => mode switch
    {
        "Зима" or "Весна" or "Лето" or "Осень" => mode,
        _ => DateTime.Now.Month switch
        {
            12 or 1 or 2 => "Зима",
            3 or 4 or 5 => "Весна",
            6 or 7 or 8 => "Лето",
            _ => "Осень"
        }
    };

    private Particle CreateParticle(string season)
    {
        double size = season == "Зима" ? 5 + _random.NextDouble() * 6 : 7 + _random.NextDouble() * 8;
        var rotation = new RotateTransform(_random.NextDouble() * 360);
        Geometry geometry = new PathGeometry(new List<PathFigure>
        {
            new PathFigure(
                new Point(0, -size),
                new List<PathSegment>
                {
                    new LineSegment { Point = new Point(size * 0.35, -size * 0.35) },
                    new LineSegment { Point = new Point(size, 0) },
                    new LineSegment { Point = new Point(size * 0.35, size * 0.35) },
                    new LineSegment { Point = new Point(0, size) },
                    new LineSegment { Point = new Point(-size * 0.35, size * 0.35) },
                    new LineSegment { Point = new Point(-size, 0) },
                    new LineSegment { Point = new Point(-size * 0.35, -size * 0.35) },
                    new LineSegment { Point = new Point(0, -size) }
                },
                true)
        });
        if (season == "Зима")
            geometry = new EllipseGeometry(new Point(0, 0), size / 2, size / 2);
        var shape = new System.Windows.Shapes.Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            Opacity = 0.10 + _random.NextDouble() * 0.18,
            Fill = new SolidColorBrush(season == "Зима"
                ? Color.FromRgb(235, 245, 255)
                : Colors.White),
            RenderTransform = rotation,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        Particles.Children.Add(shape);
        double width = Math.Max(400, ActualWidth);
        double height = Math.Max(400, ActualHeight);
        var particle = new Particle
        {
            Element = shape,
            Rotation = rotation,
            X = _random.NextDouble() * width,
            Y = _random.NextDouble() * height,
            Speed = 18 + _random.NextDouble() * (season == "Зима" ? 45 : 30),
            Drift = (0.5 + _random.NextDouble()) * (_random.Next(2) == 0 ? -1 : 1) * 18,
            Spin = (_random.NextDouble() - 0.5) * (season == "Зима" ? 25 : 90),
            Phase = _random.NextDouble() * Math.PI * 2
        };
        Canvas.SetLeft(shape, particle.X);
        Canvas.SetTop(shape, particle.Y);
        return particle;
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_particles.Count == 0) return;
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        _elapsed += 0.033;
        foreach (var particle in _particles)
        {
            particle.Y += particle.Speed * 0.033;
            particle.X += (particle.Drift + Math.Sin(_elapsed + particle.Phase) * 9) * 0.033;
            particle.Rotation.Angle += particle.Spin * 0.033;

            if (particle.Y > height + 30)
            {
                particle.Y = -30;
                particle.X = _random.NextDouble() * width;
            }
            if (particle.X < -40) particle.X = width + 20;
            if (particle.X > width + 40) particle.X = -20;

            Canvas.SetLeft(particle.Element, particle.X);
            Canvas.SetTop(particle.Element, particle.Y);
        }

        if (SunGlow.Visibility == Visibility.Visible)
            SunGlow.Opacity = 0.22 + 0.08 * Math.Sin(_elapsed * 0.7);
    }
}
