namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Backend for video generation (e.g. LTX-2 via ComfyUI).</summary>
public interface IVideoGenerationBackend
{
    Task<VideoGenerationResult> GenerateAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default);
    Task<GenerationProgress?> GetProgressAsync(string jobId, CancellationToken cancellationToken = default);
}
