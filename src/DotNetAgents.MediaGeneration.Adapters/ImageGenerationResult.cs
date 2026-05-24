namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Result of an image generation call.</summary>
public sealed class ImageGenerationResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Seed { get; init; }
    public string? WorkerId { get; init; }
    public long RenderTimeMs { get; init; }
}
