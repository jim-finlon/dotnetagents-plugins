namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>GPU capability and identity for routing (e.g. horus, thoth, zeus). Optional PreferredQualityTiers for Phase 6 fleet (High/Cinema → RTX 6000).</summary>
public sealed class GpuCapability
{
    public string WorkerId { get; init; } = string.Empty;
    public string GpuName { get; init; } = string.Empty;
    public bool SupportsVideo { get; init; }
    public bool SupportsImage { get; init; }
    public bool SupportsUpscale { get; init; }
    public bool SupportsInterpolation { get; init; }
    public long VramTotalMb { get; init; }
    public long VramUsedMb { get; init; }
    public int QueueDepth { get; init; }
    /// <summary>When set, this worker is preferred for these quality tiers (e.g. High, Cinema for RTX 6000 / HunyuanVideo).</summary>
    public IReadOnlyList<string>? PreferredQualityTiers { get; init; }
}
