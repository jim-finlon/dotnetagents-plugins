namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>Per-actor budget options. Bound from the <c>VideoBudget</c> section.</summary>
public sealed class VideoBudgetOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "VideoBudget";

    /// <summary>
    /// Per-actor envelopes. The public package ships an empty default so no private factory
    /// cost envelopes or calibrated values are exposed.
    /// </summary>
    public List<VideoBudgetActorEnvelope> Actors { get; init; } = new();

    /// <summary>Rolling accounting window length. Default 24h.</summary>
    public TimeSpan WindowLength { get; init; } = TimeSpan.FromDays(1);
}

/// <summary>One per-actor envelope entry from configuration.</summary>
public sealed class VideoBudgetActorEnvelope
{
    /// <summary>Actor id this envelope applies to.</summary>
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Hard upper bound on GPU-seconds spent within the rolling window.</summary>
    public long GpuSecondsBudget { get; init; }
}
