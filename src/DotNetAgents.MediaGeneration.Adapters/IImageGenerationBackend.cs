namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Backend for image generation (e.g. SDXL via ComfyUI).</summary>
public interface IImageGenerationBackend
{
    Task<ImageGenerationResult> GenerateAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);
    Task<GenerationProgress?> GetProgressAsync(string jobId, CancellationToken cancellationToken = default);
}
