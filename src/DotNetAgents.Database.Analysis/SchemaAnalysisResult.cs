using DotNetAgents.Database.Abstractions;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// Result of a database schema analysis operation.
/// </summary>
public sealed class SchemaAnalysisResult
{
    /// <summary>
    /// Gets the analyzed database schema.
    /// </summary>
    public required DatabaseSchema Schema { get; init; }

    /// <summary>
    /// Gets the duration of the analysis operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets any warnings encountered during analysis.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets any errors encountered during analysis.
    /// </summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the analysis was successful.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Gets the number of objects analyzed.
    /// </summary>
    public int ObjectsAnalyzed => Schema.TotalObjectCount;
}
