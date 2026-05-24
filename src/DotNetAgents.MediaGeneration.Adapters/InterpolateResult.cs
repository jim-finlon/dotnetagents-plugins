namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Result of an interpolation call.</summary>
public sealed class InterpolateResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public double DurationSeconds { get; init; }
    public string? WorkerId { get; init; }
    public long RenderTimeMs { get; init; }
}
