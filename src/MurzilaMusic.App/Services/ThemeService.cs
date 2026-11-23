using System.Windows;
using System.Windows.Media;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Переключение палитры без перезапуска. Все кисти в App.xaml и окнах используются через
/// DynamicResource, поэтому достаточно заменить ресурсы — открытые окна обновятся сами.
/// </summary>
public static class ThemeService
{
    public const string Dark = "Тёмная";
    public const string Light = "Светлая";
    public const string Midnight = "Ночная";

    public static IReadOnlyList<string> Names => new[] { Dark, Light, Midnight };

    public static void Apply(string? theme)
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        switch (theme)
        {
            case Light:
                Set(resources, "BgBrush", "#F2F4F9");
                Set(resources, "PanelBrush", "#FFFFFF");
                Set(resources, "CardBrush", "#EAEEF6");
                Set(resources, "SurfaceBrush", "#F7F9FC");
                Set(resources, "TextBrush", "#161A22");
                Set(resources, "MutedBrush", "#5F6878");
                Set(resources, "AccentBrush", "#0B9B4A");
                Set(resources, "AccentHoverBrush", "#0BB353");
                Set(resources, "SecondaryAccentBrush", "#7048D8");
                Set(resources, "GoldBrush", "#A57A16");

                Set(resources, "BorderBrush", "#D6DCE8");
                Set(resources, "BorderSoftBrush", "#E3E8F2");
                Set(resources, "SubtleMutedBrush", "#6F7887");
                Set(resources, "RowHoverBrush", "#EDF1F8");
                Set(resources, "RowSelectedBrush", "#E0F5E8");
                Set(resources, "InputBrush", "#FFFFFF");
                Set(resources, "FavoriteBrush", "#E23E63");
                Set(resources, "DangerBrush", "#C02040");
                Set(resources, "OnAccentBrush", "#FFFFFF");
                Set(resources, "PromoAccentBrush", "#6B3FD4");
                Set(resources, "PromoTextBrush", "#39414F");
                Set(resources, "PromoButtonBrush", "#B0FFFFFF");
                Set(resources, "PromoButtonHoverBrush", "#FFFFFFFF");
                Set(resources, "HeroKickerBrush", "#14532D");
                Set(resources, "HeroTextBrush", "#2A3242");
                Set(resources, "HeroButtonBrush", "#66FFFFFF");
                Set(resources, "HeroButtonBorderBrush", "#99FFFFFF");
                Set(resources, "OwnNoteBrush", "#E9F5EE");
                Set(resources, "OwnNoteTextBrush", "#2F6B4A");
                Set(resources, "OverlayBrush", "#D9F2F4F9");

                Gradient(resources, "AccentGradient", false, "#0B9B4A", "#0B8E7A");
                Gradient(resources, "PremiumGradient", false, "#7048D8", "#C0357E");
                Gradient(resources, "HeroGradient", false, "#D8F3E2", "#DCE7F8", "#EDE0FA");
                Gradient(resources, "TopLineBrush", true, "#0B9B4A", "#0B8E7A", "#7048D8");
                Gradient(resources, "HeaderGradient", true, "#FFFFFF", "#F4F7FC", "#EEF1FB");
                Gradient(resources, "HeaderBorderGradient", true, "#CDEBD9", "#DDE3EF", "#E3D8F4");
                Gradient(resources, "PromoGradient", false, "#EAE4FB", "#E1EBFB", "#DFF4EA");
                Gradient(resources, "NowPlayingGradient", false, "#EFF7F1", "#F4F6FB", "#F1EAFB");
                Gradient(resources, "PlayerGradient", true, "#FFFFFF", "#F8FAFE", "#F2EEFA");
                Gradient(resources, "MenuGradient", false, "#FFFFFF", "#F5F7FC");
                break;

            case Midnight:
                Set(resources, "BgBrush", "#000000");
                Set(resources, "PanelBrush", "#08080B");
                Set(resources, "CardBrush", "#101015");
                Set(resources, "SurfaceBrush", "#16161D");
                Set(resources, "TextBrush", "#F5F5F7");
                Set(resources, "MutedBrush", "#8E8E9B");
                Set(resources, "AccentBrush", "#29E175");
                Set(resources, "AccentHoverBrush", "#5AFF96");
                Set(resources, "SecondaryAccentBrush", "#9B6BFF");
                Set(resources, "GoldBrush", "#F5D98F");

                Set(resources, "BorderBrush", "#24242E");
                Set(resources, "BorderSoftBrush", "#16161D");
                Set(resources, "SubtleMutedBrush", "#7C7C8A");
                Set(resources, "RowHoverBrush", "#14141B");
                Set(resources, "RowSelectedBrush", "#173224");
                Set(resources, "InputBrush", "#101015");
                Set(resources, "FavoriteBrush", "#FF5C7A");
                Set(resources, "DangerBrush", "#FF8B8B");
                Set(resources, "OnAccentBrush", "#06120B");
                Set(resources, "PromoAccentBrush", "#C9A7FF");
                Set(resources, "PromoTextBrush", "#C4C4D6");
                Set(resources, "PromoButtonBrush", "#1AFFFFFF");
                Set(resources, "PromoButtonHoverBrush", "#2EFFFFFF");
                Set(resources, "HeroKickerBrush", "#B8F5CC");
                Set(resources, "HeroTextBrush", "#D0D8E4");
                Set(resources, "HeroButtonBrush", "#18FFFFFF");
                Set(resources, "HeroButtonBorderBrush", "#2AFFFFFF");
                Set(resources, "OwnNoteBrush", "#0E1A14");
                Set(resources, "OwnNoteTextBrush", "#7E9A8B");
                Set(resources, "OverlayBrush", "#E0000000");

                Gradient(resources, "AccentGradient", false, "#29E175", "#14D8A8");
                Gradient(resources, "PremiumGradient", false, "#9B6BFF", "#F056A6");
                Gradient(resources, "HeroGradient", false, "#0B2419", "#071A2A", "#1C0F33");
                Gradient(resources, "TopLineBrush", true, "#29E175", "#14D8A8", "#9B6BFF");
                Gradient(resources, "HeaderGradient", true, "#050507", "#08080C", "#0D0817");
                Gradient(resources, "HeaderBorderGradient", true, "#123324", "#1A1A22", "#241338");
                Gradient(resources, "PromoGradient", false, "#1B1133", "#0E1A2E", "#0A2A22");
                Gradient(resources, "NowPlayingGradient", false, "#07120F", "#000000", "#120A22");
                Gradient(resources, "PlayerGradient", true, "#07070A", "#0B0B10", "#0D0817");
                Gradient(resources, "MenuGradient", false, "#0D0D12", "#08080B");
                break;

            default:
                Set(resources, "BgBrush", "#0B0B12");
                Set(resources, "PanelBrush", "#14141D");
                Set(resources, "CardBrush", "#1C1C28");
                Set(resources, "SurfaceBrush", "#232331");
                Set(resources, "TextBrush", "#FFFFFF");
                Set(resources, "MutedBrush", "#A7A7B8");
                Set(resources, "AccentBrush", "#1ED760");
                Set(resources, "AccentHoverBrush", "#3BE87C");
                Set(resources, "SecondaryAccentBrush", "#8B5CF6");
                Set(resources, "GoldBrush", "#F0D68B");

                Set(resources, "BorderBrush", "#2A2A3C");
                Set(resources, "BorderSoftBrush", "#20202E");
                Set(resources, "SubtleMutedBrush", "#8A8AA0");
                Set(resources, "RowHoverBrush", "#1E2030");
                Set(resources, "RowSelectedBrush", "#263047");
                Set(resources, "InputBrush", "#1B1B27");
                Set(resources, "FavoriteBrush", "#FF5C7A");
                Set(resources, "DangerBrush", "#FF8B8B");
                Set(resources, "OnAccentBrush", "#07160D");
                Set(resources, "PromoAccentBrush", "#C9A7FF");
                Set(resources, "PromoTextBrush", "#C4C4D6");
                Set(resources, "PromoButtonBrush", "#22FFFFFF");
                Set(resources, "PromoButtonHoverBrush", "#33FFFFFF");
                Set(resources, "HeroKickerBrush", "#B8F5CC");
                Set(resources, "HeroTextBrush", "#D0D8E4");
                Set(resources, "HeroButtonBrush", "#20FFFFFF");
                Set(resources, "HeroButtonBorderBrush", "#33FFFFFF");
                Set(resources, "OwnNoteBrush", "#141C1B");
                Set(resources, "OwnNoteTextBrush", "#7E9A8B");
                Set(resources, "OverlayBrush", "#D90B0B12");

                Gradient(resources, "AccentGradient", false, "#1ED760", "#14C8A0");
                Gradient(resources, "PremiumGradient", false, "#8B5CF6", "#EC4899");
                Gradient(resources, "HeroGradient", false, "#1E5A3E", "#16304A", "#2A1B4D");
                Gradient(resources, "TopLineBrush", true, "#1ED760", "#14C8A0", "#8B5CF6");
                Gradient(resources, "HeaderGradient", true, "#11121A", "#14141F", "#171326");
                Gradient(resources, "HeaderBorderGradient", true, "#1E3B2C", "#2A2A3A", "#3B2A5A");
                Gradient(resources, "PromoGradient", false, "#2A1B4D", "#1B2A4A", "#12463A");
                Gradient(resources, "NowPlayingGradient", false, "#0E1A17", "#0B0B12", "#1A1230");
                Gradient(resources, "PlayerGradient", true, "#12121B", "#161620", "#14101F");
                Gradient(resources, "MenuGradient", false, "#1C1C2C", "#14141F");
                break;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static void Gradient(ResourceDictionary resources, string key, bool horizontal, params string[] colors)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = horizontal ? new Point(1, 0) : new Point(1, 1)
        };
        foreach (string hex in colors)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(hex), 0));
        for (int i = 0; i < brush.GradientStops.Count; i++)
            brush.GradientStops[i].Offset = colors.Length == 1 ? 0 : (double)i / (colors.Length - 1);
        resources[key] = brush;
    }
}
