namespace MurzilaMusic.Shared;

/// <summary>Общий каталог тарифов. Используется и сервером, и WPF-клиентом.</summary>
public sealed class CatalogDto
{
    public string Version { get; set; } = "";
    public string Currency { get; set; } = "RUB";
    public List<string> AdsLevels { get; set; } = new();
    public List<PlanDto> Plans { get; set; } = new();
    public List<AddonDto> Addons { get; set; } = new();
    public List<FeatureDto> Features { get; set; } = new();
}

public sealed class PlanDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceRub { get; set; }
    public int TrackLimit { get; set; }
    public int MinuteLimit { get; set; }
    public string AdsLevel { get; set; } = "full";
    public int? DurationDays { get; set; }
    public int FeatureCount { get; set; }
    public int SortOrder { get; set; }
    public int LegacyTariff { get; set; }
    public string Description { get; set; } = "";
    public List<string> Features { get; set; } = new();

    public string LimitText => TrackLimit < 0 ? "без лимита треков" : $"до {TrackLimit:N0} треков";
    public string DurationText => MinuteLimit < 0 ? "любая длительность" : $"до {MinuteLimit} мин";
    public string AdsText => AdsLevel switch
    {
        "none" => "без рекламы",
        "low" => "мало рекламы",
        _ => "с рекламой"
    };
}

public sealed class AddonDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int PriceRub { get; set; }
    public string Kind { get; set; } = "";
    public int SortOrder { get; set; }
    public int LegacyTariff { get; set; }
    public string Description { get; set; } = "";
}

public sealed class FeatureDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "available";

    public bool Planned => string.Equals(Status, "planned", StringComparison.OrdinalIgnoreCase);
}
