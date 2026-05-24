namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Result of an audio generation call.</summary>
public sealed class AudioGenerationResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public double DurationSeconds { get; init; }
}
