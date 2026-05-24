namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Request for frame interpolation (e.g. RIFE).</summary>
public sealed class InterpolateRequest
{
    public string InputPath { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public int TargetFps { get; init; } = 24;
}
