using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MurzilaMusic.App.Converters;

/// <summary>Путь к обложке -> картинка. Если файла нет, берём иконку Murzila-Music.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    private static readonly BitmapImage Fallback = CreateFallback();

    private static BitmapImage CreateFallback()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "murzila-icon.png"), UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Fallback;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            return image;
        }
        catch
        {
            return Fallback;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}
