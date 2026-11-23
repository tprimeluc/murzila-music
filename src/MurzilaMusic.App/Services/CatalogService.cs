using System.Text.Json;
using MurzilaMusic.App.Models;
using MurzilaMusic.Shared;

namespace MurzilaMusic.App.Services;

/// <summary>Каталог тарифов и 48 функций. Работает офлайн, при наличии сервера обновляется с /api/catalog.</summary>
public static class CatalogService
{
    private static readonly string CacheFile = Path.Combine(AppPaths.Root, "catalog.cache.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static CatalogDto _current = Load();

    public static CatalogDto Current => _current;
    public static string Source { get; private set; } = "встроенный каталог";

    private static CatalogDto Load()
    {
        try
        {
            if (File.Exists(CacheFile))
            {
                var cached = JsonSerializer.Deserialize<CatalogDto>(File.ReadAllText(CacheFile), Json);
                if (cached is not null && CatalogLoader.Validate(cached).Count == 0)
                {
                    Source = "кэш каталога";
                    return cached;
                }
            }
        }
        catch { }
        Source = "встроенный каталог";
        return CatalogLoader.LoadEmbedded();
    }

    public static async Task RefreshAsync()
    {
        try
        {
            if (!await ApiClient.IsAvailableAsync()) return;
            var fresh = await ApiClient.GetCatalogAsync();
            if (CatalogLoader.Validate(fresh).Count > 0) return;
            _current = fresh;
            Source = "сервер";
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(fresh, Json));
        }
        catch
        {
            // Офлайн-каталог уже загружен — плеер должен работать без сервера.
        }
    }

    public static PlanDto PlanForLegacyTariff(int tariff) => Current.PlanForLegacyTariff(tariff);

    public static PlanDto FindPlan(string id) =>
        Current.FindPlan(id) ?? Current.Plans.First(p => p.LegacyTariff == 0);

    /// <summary>Применяет состояние магазина к локальным настройкам и тарифу.</summary>
    public static void Apply(ServerAccountState state)
    {
        var settings = SettingsService.Current;
        settings.Tariff = (Tariff)state.User.Tariff;
        settings.ActivePlanId = state.ActivePlanId;
        settings.AdsLevel = state.AdsLevel;
        settings.BalanceMinor = state.BalanceMinor;
        settings.ActiveFeatureIds = state.Features.ToList();
        settings.UnlockedTracks = state.UnlockedTrackIds.ToList();
        SettingsService.Save();
    }
}
