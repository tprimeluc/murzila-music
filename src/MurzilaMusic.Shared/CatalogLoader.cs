using System.Reflection;
using System.Text.Json;

namespace MurzilaMusic.Shared;

/// <summary>Читает встроенный plans.json. Один и тот же файл компилируется в сервер и клиент.</summary>
public static class CatalogLoader
{
    public const string ResourceName = "MurzilaMusic.Catalog.plans.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CatalogDto LoadEmbedded()
    {
        using var stream = typeof(CatalogLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Каталог планов не встроен в сборку: " + ResourceName);
        var catalog = JsonSerializer.Deserialize<CatalogDto>(stream, Json)
            ?? throw new InvalidOperationException("Не удалось прочитать каталог планов.");
        return catalog;
    }

    public static IReadOnlyList<string> Validate(CatalogDto catalog)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(catalog.Version)) errors.Add("Нет version.");
        if (catalog.Plans.Count == 0) errors.Add("Каталог не содержит планов.");
        if (catalog.Features.Count == 0) errors.Add("Каталог не содержит функций.");

        var featureIds = catalog.Features.Select(f => f.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (featureIds.Count != catalog.Features.Count) errors.Add("Дубли id функций.");

        foreach (var plan in catalog.Plans)
        {
            if (string.IsNullOrWhiteSpace(plan.Id)) errors.Add("План без id.");
            if (plan.Features.Count != plan.FeatureCount)
                errors.Add($"У плана {plan.Id} featureCount={plan.FeatureCount}, но список функций содержит {plan.Features.Count}.");
            if (plan.Features.Any(id => !featureIds.Contains(id)))
                errors.Add($"У плана {plan.Id} есть неизвестные функции.");
            if (plan.Features.Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Features.Count)
                errors.Add($"У плана {plan.Id} есть повторяющиеся функции.");
        }

        var sorted = catalog.Plans.OrderBy(p => p.FeatureCount).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            var previous = sorted[i - 1].Features.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!previous.IsSubsetOf(sorted[i].Features))
                errors.Add($"Функции плана {sorted[i - 1].Id} не входят в план {sorted[i].Id}.");
        }

        var ultra = sorted.LastOrDefault();
        if (ultra is not null && ultra.FeatureCount != catalog.Features.Count)
            errors.Add($"Старший план должен содержать все функции каталога ({catalog.Features.Count}), сейчас {ultra.FeatureCount}.");

        return errors;
    }

    public static PlanDto? FindPlan(this CatalogDto catalog, string id) =>
        catalog.Plans.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static AddonDto? FindAddon(this CatalogDto catalog, string id) =>
        catalog.Addons.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Совместимость со старым числовым тарифом в SQLite и settings.json.</summary>
    public static PlanDto PlanByLegacyTariff(this CatalogDto catalog, int tariff) =>
        catalog.Plans.FirstOrDefault(p => p.LegacyTariff == tariff)
        ?? catalog.Plans.First(p => p.LegacyTariff == 0);

    /// <summary>Лучший план, который даёт указанный старый тариф. 5 ₽ — отдельный аддон, не план.</summary>
    public static PlanDto PlanForLegacyTariff(this CatalogDto catalog, int tariff)
    {
        var exact = catalog.Plans.FirstOrDefault(p => p.LegacyTariff == tariff);
        if (exact is not null) return exact;
        return catalog.Plans
            .Where(p => p.LegacyTariff <= tariff)
            .OrderByDescending(p => p.LegacyTariff)
            .FirstOrDefault()
            ?? catalog.Plans.First(p => p.LegacyTariff == 0);
    }
}
