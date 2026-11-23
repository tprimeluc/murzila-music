using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Собственные векторные иконки приложения. Это не символы шрифта и не эмодзи:
/// каждая иконка — нарисованный контур в сетке 24×24. Цвет берётся у кнопки,
/// поэтому иконки автоматически перекрашиваются в тёмной, светлой и ночной теме.
/// </summary>
public static class AppIcons
{
    private const double DefaultStroke = 2.2;

    private static readonly Dictionary<string, string> Data = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "M12,3 L21,10 L21,21 L14,21 L14,14 L10,14 L10,21 L3,21 L3,10 Z",
        ["music"] = "M4.6,18 A2.2,2.2 0 1 1 9,18 A2.2,2.2 0 1 1 4.6,18 Z M7,5 L9,5 L9,18 L7,18 Z M14.6,16 A2.2,2.2 0 1 1 19,16 A2.2,2.2 0 1 1 14.6,16 Z M17,3 L19,3 L19,16 L17,16 Z M7,5 L19,3 L19,6 L7,8 Z",
        ["heart"] = "M12,21 C12,21 3,15.5 3,9.2 C3,6 5.4,4 8.1,4 C10,4 11.4,5.1 12,6.4 C12.6,5.1 14,4 15.9,4 C18.6,4 21,6 21,9.2 C21,15.5 12,21 12,21 Z",
        ["heartOutline"] = "M12,20.1 C12,20.1 4,15 4,9.2 C4,6.4 6.2,4.8 8.4,4.8 C10.1,4.8 11.5,5.8 12,7.2 C12.5,5.8 13.9,4.8 15.6,4.8 C17.8,4.8 20,6.4 20,9.2 C20,15 12,20.1 12,20.1 Z",
        ["headphones"] = "M4,13 A8,8 0 0 1 20,13 L20,16 A4,4 0 0 1 16,20 L15,20 L15,14 L18,14 L18,13 A6,6 0 0 0 6,13 L6,14 L9,14 L9,20 L8,20 A4,4 0 0 1 4,16 Z",
        ["import"] = "M12,3 L12,12 M7.5,7.5 L12,12 L16.5,7.5 M4,16 L4,20 L20,20 L20,16",
        ["backup"] = "M4,7 L20,7 L20,20 L4,20 Z M3,4 L21,4 L21,7 L3,7 Z M9,11 L15,11",
        ["cloud"] = "M6,18 A4,4 0 0 1 6,10 A6,6 0 0 1 17.5,9.5 A3.5,3.5 0 0 1 17,16.5 L17,18 Z",
        ["sparkle"] = "M12,2 L14,9 L21,11 L14,13 L12,20 L10,13 L3,11 L10,9 Z",
        ["plus"] = "M10.5,3.5 L13.5,3.5 L13.5,10.5 L20.5,10.5 L20.5,13.5 L13.5,13.5 L13.5,20.5 L10.5,20.5 L10.5,13.5 L3.5,13.5 L3.5,10.5 L10.5,10.5 Z",
        ["search"] = "M10.5,4 A6.5,6.5 0 1 1 10.5,17 A6.5,6.5 0 1 1 10.5,4 Z M15.2,15.2 L21,21",
        ["settings"] = "M12,8.4 A3.6,3.6 0 1 0 12,15.6 A3.6,3.6 0 1 0 12,8.4 Z M10.4,2.4 L13.6,2.4 L14.2,5.2 L16,6 L18.6,4.5 L20.5,7.6 L18.3,9.4 L18.3,11.6 L20.5,13.4 L18.6,16.5 L16,15 L14.2,15.8 L13.6,18.6 L10.4,18.6 L9.8,15.8 L8,15 L5.4,16.5 L3.5,13.4 L5.7,11.6 L5.7,9.4 L3.5,7.6 L5.4,4.5 L8,6 L9.8,5.2 Z",
        ["user"] = "M12,3 A4,4 0 1 1 12,11 A4,4 0 1 1 12,3 Z M4,21 C4,16.5 7.5,13.5 12,13.5 C16.5,13.5 20,16.5 20,21 Z",
        ["play"] = "M8,5 L19,12 L8,19 Z",
        ["pause"] = "M7,5 L10,5 L10,19 L7,19 Z M14,5 L17,5 L17,19 L14,19 Z",
        ["previous"] = "M6,5 L8,5 L8,19 L6,19 Z M19,5 L19,19 L8.5,12 Z",
        ["next"] = "M16,5 L18,5 L18,19 L16,19 Z M5,5 L15.5,12 L5,19 Z",
        ["shuffle"] = "M4,7 L8,7 L20,17 M16,17 L20,17 L20,13 M4,17 L8,17 L20,7 M16,7 L20,7 L20,11",
        ["repeat"] = "M20,11 A8,8 0 1 0 18.4,15.8 M20,5 L20,11 L14,11",
        ["repeatOne"] = "M20,11 A8,8 0 1 0 18.4,15.8 M20,5 L20,11 L14,11 M11,10.2 L12.6,9.2 L12.6,15.2 M10.8,15.2 L14.4,15.2",
        ["volume"] = "M4,10 L8,10 L13,6 L13,18 L8,14 L4,14 Z M16,9 A4,4 0 0 1 16,15 M18,7 A7,7 0 0 1 18,17",
        ["mute"] = "M4,10 L8,10 L13,6 L13,18 L8,14 L4,14 Z M16,9 L21,15 M21,9 L16,15",
        ["lyrics"] = "M5,3 L15,3 L19,7 L19,21 L5,21 Z M15,3 L15,7 L19,7 M8,11 L16,11 M8,15 L16,15 M8,18 L13,18",
        ["video"] = "M3,6 L21,6 L21,18 L3,18 Z M10,9 L16,12 L10,15 Z",
        ["equalizer"] = "M6,4 L6,20 M12,4 L12,20 M18,4 L18,20 M4,8 L8,8 M10,15 L14,15 M16,7 L20,7",
        ["download"] = "M12,3 L12,15 M7,10 L12,15 L17,10 M5,19 L19,19",
        ["share"] = "M14,4 L20,4 L20,10 M20,4 L11,13 M8,6 L4,6 L4,20 L18,20 L18,16",
        ["edit"] = "M4,20 L5,15 L16,4 L20,8 L9,19 Z M14,6 L18,10",
        ["folder"] = "M3,6 L10,6 L12,8 L21,8 L21,20 L3,20 Z",
        ["back"] = "M19,12 L5,12 M11,6 L5,12 L11,18",
        ["sort"] = "M6,9 L18,9 L12,16 Z",
        ["close"] = "M6,6 L18,18 M18,6 L6,18",
        ["fullscreen"] = "M4,9 L4,4 L9,4 M15,4 L20,4 L20,9 M20,15 L20,20 L15,20 M9,20 L4,20 L4,15",
        ["shield"] = "M12,3 L20,6 L20,12 C20,17 16.4,20.6 12,22 C7.6,20.6 4,17 4,12 L4,6 Z M8.6,12.2 L11,14.6 L15.6,9.6",
        ["tag"] = "M3,12 L11.4,3.6 L21,3.6 L21,13.2 L12.6,21.6 Z M16.6,7.4 A1,1 0 1 1 17,7.4 A1,1 0 1 1 16.6,7.4 Z",
        ["sync"] = "M20,9 A8,8 0 0 0 6.3,6.3 M4,15 A8,8 0 0 0 17.7,17.7 M20,4 L20,9 L15,9 M4,20 L4,15 L9,15",
        ["seekBack"] = "M11,6 L4,12 L11,18 Z M20,6 L13,12 L20,18 Z",
        ["seekForward"] = "M13,6 L20,12 L13,18 Z M4,6 L11,12 L4,18 Z",
        ["copy"] = "M8,8 L20,8 L20,20 L8,20 Z M5,16 L5,4 L16,4",
        ["paste"] = "M9,4 L15,4 L15,7 L9,7 Z M7,6 L4,6 L4,21 L20,21 L20,6 L17,6 M9,12 L15,12 M9,16 L15,16",
        ["info"] = "M12,3 A9,9 0 1 1 12,21 A9,9 0 1 1 12,3 Z M12,10.8 L12,16.6 M12,7.4 L12,7.6"
    };

    private static readonly HashSet<string> Filled = new(StringComparer.OrdinalIgnoreCase)
    {
        "home", "music", "heart", "headphones", "cloud", "sparkle", "plus", "settings",
        "user", "play", "pause", "previous", "next", "sort", "seekBack", "seekForward"
    };

    public static Geometry Get(string key) =>
        Data.TryGetValue(key, out string? path) ? Geometry.Parse(path) : Geometry.Empty;

    /// <summary>Готовая иконка для Content кнопки. Цвет наследуется от Foreground кнопки.</summary>
    public static Path Create(string key, double size = 18, double stroke = DefaultStroke)
    {
        bool filled = Filled.Contains(key);
        var path = new Path
        {
            Data = Get(key),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            StrokeThickness = stroke,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var binding = new Binding(nameof(Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        };

        if (filled)
        {
            BindingOperations.SetBinding(path, Shape.FillProperty, binding);
        }
        else
        {
            path.Fill = Brushes.Transparent;
            BindingOperations.SetBinding(path, Shape.StrokeProperty, binding);
        }

        return path;
    }

    /// <summary>Иконка для текстового блока или другого места, где нет кнопки-родителя.</summary>
    public static Path CreateMuted(string key, double size = 16, string resourceKey = "MutedBrush")
    {
        var path = Create(key, size);
        bool filled = Filled.Contains(key);
        if (filled)
            path.SetResourceReference(Shape.FillProperty, resourceKey);
        else
            path.SetResourceReference(Shape.StrokeProperty, resourceKey);
        return path;
    }

    /// <summary>Иконка + текст для обычной кнопки с подписью.</summary>
    public static StackPanel WithText(string key, string text, double size = 18)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(Create(key, size));
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Margin = new Thickness(11, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }
}

/// <summary>Позволяет использовать нарисованные иконки прямо в XAML: Data="{svc:IconGeometry heart}".</summary>
public sealed class IconGeometryExtension : MarkupExtension
{
    public IconGeometryExtension()
    {
    }

    public IconGeometryExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => AppIcons.Get(Key);
}
