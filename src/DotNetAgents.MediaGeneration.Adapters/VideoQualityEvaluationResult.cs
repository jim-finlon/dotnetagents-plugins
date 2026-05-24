namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Input for video quality evaluation (clip path, key frames, metadata).</summary>
public sealed class VideoQualityEvaluationInput
{
    public string VideoPath { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public double DurationSeconds { get; init; }
    public IReadOnlyList<string>? KeyFramePaths { get; init; }
    public string? ScenePrompt { get; init; }
}

/// <summary>Result of quality evaluation (scores and verdict).</summary>
public sealed class VideoQualityEvaluationResult
{
    public float VisualQuality { get; init; }
    public float MotionConsistency { get; init; }
    public float ResolutionCompliance { get; init; }
    public float DurationCompliance { get; init; }
    public float TechnicalHealth { get; init; }
    public string Verdict { get; init; } = "Pending"; // Pass | Fail | NeedsHumanReview
    public string? Feedback { get; init; }
}
