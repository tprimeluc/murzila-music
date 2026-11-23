using MurzilaMusic.App.Models;

namespace MurzilaMusic.App.Services;

/// <summary>
/// Правила доступа читаются из общего каталога планов (src/MurzilaMusic.Shared/catalog/plans.json):
/// Free — 5000 треков / 6 минут, Про — 20000 / 10, Премиум — 50000 / 20, Ультра — без лимитов.
/// Если сервер не запущен, используется тот же встроенный каталог.
/// </summary>
public static class AccessPolicy
{
    public const int FreeMinutes = 6;
    public const int LoginMinutes = 10;
    public const long TracksBeforeLogin = 5000;
    public const int SingleUnlockRub = 5;

    public static AccessDecision Check(TrackItem track, bool singleUnlock, LibraryData library, AppSettings settings)
    {
        double minutes = track.DurationSeconds / 60.0;
        var result = new AccessDecision();

        if (settings.UnlockedTracks.Contains(track.Id)) { result.Allowed = true; return result; }

        var plan = CatalogService.PlanForLegacyTariff((int)settings.Tariff);
        if (plan.MinuteLimit < 0 || minutes <= plan.MinuteLimit)
        {
            if (plan.TrackLimit >= 0 && library.TotalPlayed >= plan.TrackLimit)
            {
                result.NeedsLogin = !settings.LoggedIn;
                result.NeedsPayment = plan.PriceRub > 0;
                result.AmountRub = plan.PriceRub;
                result.RequiredTariff = (Tariff)plan.LegacyTariff;
                result.Reason = plan.PriceRub == 0
                    ? $"Прослушано {plan.TrackLimit:N0}+ треков — для продолжения нужен вход."
                    : $"Лимит плана «{plan.Name}» — {plan.TrackLimit:N0} треков — исчерпан.";
                return result;
            }
            result.Allowed = true;
            return result;
        }

        var needed = CatalogService.Current.Plans
            .Where(p => p.MinuteLimit < 0 || p.MinuteLimit >= minutes)
            .OrderBy(p => p.PriceRub)
            .FirstOrDefault() ?? CatalogService.Current.Plans.Last();

        result.NeedsLogin = !settings.LoggedIn;
        result.NeedsPayment = true;
        result.AmountRub = needed.PriceRub;
        result.RequiredTariff = (Tariff)needed.LegacyTariff;
        result.Reason = needed.MinuteLimit < 0
            ? $"Трек длиннее {plan.MinuteLimit} минут. Нужен тариф «{needed.Name}» без ограничений."
            : $"Трек длиннее {plan.MinuteLimit} минут. Нужен тариф «{needed.Name}» — до {needed.MinuteLimit} минут.";
        return result;
    }
}
