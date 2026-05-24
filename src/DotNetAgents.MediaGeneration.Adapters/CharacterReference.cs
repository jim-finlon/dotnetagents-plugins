namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Reference to a character for scene prompts (from PublishingAgent manifest).</summary>
public sealed class CharacterReference
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ReferenceImagePath { get; init; }
    public string? VoiceId { get; init; }
}
