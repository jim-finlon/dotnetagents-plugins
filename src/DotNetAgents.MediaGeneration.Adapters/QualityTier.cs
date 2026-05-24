namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Public quality tiers used by generic media adapters and cost estimators.</summary>
public enum QualityTier
{
    Draft,
    Standard,
    High,
    Cinema,
}

/// <summary>Interop helpers for quality-tier wire values.</summary>
public static class QualityTierExtensions
{
    public static string ToWireString(this QualityTier tier) => tier switch
    {
        QualityTier.Draft => "Draft",
        QualityTier.Standard => "Standard",
        QualityTier.High => "High",
        QualityTier.Cinema => "Cinema",
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown quality tier"),
    };

    public static QualityTier ParseQualityTier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value.Trim().ToLowerInvariant() switch
        {
            "draft" => QualityTier.Draft,
            "standard" => QualityTier.Standard,
            "high" => QualityTier.High,
            "cinema" => QualityTier.Cinema,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                $"Unknown quality tier '{value}'. Expected one of Draft, Standard, High, Cinema."),
        };
    }
}
