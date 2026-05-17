namespace DotNetAgents.Database.AI;

/// <summary>
/// Recommendation for database type mapping.
/// </summary>
public sealed record TypeMappingRecommendation
{
    /// <summary>
    /// Gets the column name.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// Gets the original data type.
    /// </summary>
    public required string OriginalType { get; init; }

    /// <summary>
    /// Gets the recommended data type.
    /// </summary>
    public required string RecommendedType { get; init; }

    /// <summary>
    /// Gets the rationale for the recommendation.
    /// </summary>
    public required string Rationale { get; init; }

    /// <summary>
    /// Gets alternative type options.
    /// </summary>
    public List<AlternativeType> Alternatives { get; init; } = [];

    /// <summary>
    /// Gets confidence score (0-100).
    /// </summary>
    public int ConfidenceScore { get; init; }

    /// <summary>
    /// Gets storage comparison information.
    /// </summary>
    public string? StorageComparison { get; init; }

    /// <summary>
    /// Gets performance implications.
    /// </summary>
    public string? PerformanceImplications { get; init; }
}

/// <summary>
/// Represents an alternative type option.
/// </summary>
public sealed record AlternativeType
{
    /// <summary>
    /// Gets the type name.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets when to use this alternative.
    /// </summary>
    public string? UseWhen { get; init; }
}
