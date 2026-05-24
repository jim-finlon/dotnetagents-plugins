namespace DotNetAgents.MediaGeneration.Adapters;

/// <summary>Evaluates video clip quality (LLM + technical checks).</summary>
public interface IVideoQualityEvaluator
{
    Task<VideoQualityEvaluationResult> EvaluateAsync(VideoQualityEvaluationInput input, CancellationToken cancellationToken = default);
}
