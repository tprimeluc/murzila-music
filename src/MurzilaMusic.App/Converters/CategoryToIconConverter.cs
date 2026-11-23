using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MurzilaMusic.App.Services;

namespace MurzilaMusic.App.Converters;

/// <summary>Категория возможности -> нарисованная иконка AppIcons.</summary>
public sealed class CategoryToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        string key = (value as string) switch
        {
            "Аккаунт" => "user",
            "Безопасность" => "shield",
            "Музыка" => "music",
            "Магазин" => "tag",
            "Синхронизация" => "sync",
            "Система" => "settings",
            _ => "sparkle"
        };
        return AppIcons.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}
