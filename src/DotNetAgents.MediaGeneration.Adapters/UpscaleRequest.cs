namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Request for video upscaling (e.g. RealESRGAN).</summary>
public sealed class UpscaleRequest
{
    public string InputPath { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public int ScaleFactor { get; init; } = 2;
    public int? Width { get; init; }
    public int? Height { get; init; }
}
