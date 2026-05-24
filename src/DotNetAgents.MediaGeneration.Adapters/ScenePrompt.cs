namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Per-scene prompt data (from PublishingAgent manifest).</summary>
public sealed class ScenePrompt
{
    public int Order { get; init; }
    public string VisualDescription { get; init; } = string.Empty;
    public string? CameraDirection { get; init; }
    public string? Mood { get; init; }
    public string? Lighting { get; init; }
    public AudioDirection? Audio { get; init; }
    public IReadOnlyList<CharacterReference>? CharacterRefs { get; init; }
    public string? ReferenceImagePath { get; init; }
}
