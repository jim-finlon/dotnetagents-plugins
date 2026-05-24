namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Request for video generation (T2V or I2V). Optional QualityTier for fleet routing (High/Cinema → preferred worker).</summary>
public sealed class VideoGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;
    public string? NegativePrompt { get; init; }
    public bool IsImageToVideo { get; init; }
    public string? InputImagePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double DurationSeconds { get; init; }
    public int Seed { get; init; }
    public string? PreferredWorkerId { get; init; }
    /// <summary>Quality tier (Draft, Standard, High, Cinema) for fleet routing when configured.</summary>
    public string? QualityTier { get; init; }
}
