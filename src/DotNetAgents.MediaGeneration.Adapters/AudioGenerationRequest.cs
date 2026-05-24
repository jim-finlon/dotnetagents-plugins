namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Request for audio generation (e.g. XTTS narration).</summary>
public sealed class AudioGenerationRequest
{
    public string Text { get; init; } = string.Empty;
    public string? VoiceId { get; init; }
    public string? Style { get; init; }
}
