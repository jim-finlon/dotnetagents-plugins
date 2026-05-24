namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Result of an upscale call.</summary>
public sealed class UpscaleResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? WorkerId { get; init; }
    public long RenderTimeMs { get; init; }
}
