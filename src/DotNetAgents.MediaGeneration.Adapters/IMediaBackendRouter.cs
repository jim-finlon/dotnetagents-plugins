namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Routes generation requests to the appropriate backend/worker (e.g. by job type and GPU).</summary>
public interface IMediaBackendRouter
{
    Task<VideoGenerationResult> RouteVideoAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default);
    Task<ImageGenerationResult> RouteImageAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);
    Task<AudioGenerationResult> RouteAudioAsync(AudioGenerationRequest request, CancellationToken cancellationToken = default);
    Task<UpscaleResult> RouteUpscaleAsync(UpscaleRequest request, CancellationToken cancellationToken = default);
    Task<InterpolateResult> RouteInterpolateAsync(InterpolateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GpuCapability>> GetGpuStatusAsync(CancellationToken cancellationToken = default);
}
