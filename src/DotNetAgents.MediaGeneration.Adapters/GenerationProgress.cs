namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Progress update during generation (e.g. ComfyUI progress).</summary>
public sealed class GenerationProgress
{
    public string JobId { get; init; } = string.Empty;
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public string? CurrentNode { get; init; }
    public double ProgressPercent => TotalSteps > 0 ? (CurrentStep * 100.0 / TotalSteps) : 0;
}
