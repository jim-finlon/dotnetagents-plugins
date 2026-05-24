namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Audio direction for a scene (narration, music).</summary>
public sealed class AudioDirection
{
    public string? NarrationText { get; init; }
    public string? VoiceId { get; init; }
    public string? MusicStyle { get; init; }
    public float? MusicVolume { get; init; }
}
