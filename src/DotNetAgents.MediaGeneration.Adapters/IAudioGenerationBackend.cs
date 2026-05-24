namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Backend for audio generation (e.g. XTTS v2).</summary>
public interface IAudioGenerationBackend
{
    Task<AudioGenerationResult> GenerateAsync(AudioGenerationRequest request, CancellationToken cancellationToken = default);
}
