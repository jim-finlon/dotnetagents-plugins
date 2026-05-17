namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Interface for analyzing database schemas and extracting structural information.
/// </summary>
public interface IDatabaseSchemaAnalyzer
{
    /// <summary>
    /// Analyzes a database and extracts its complete schema.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="options">Optional analysis options.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The complete database schema.</returns>
    Task<DatabaseSchema> AnalyzeAsync(
        string connectionString,
        SchemaAnalysisOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for schema analysis.
/// </summary>
public sealed class SchemaAnalysisOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to include system objects in the analysis.
    /// </summary>
    public bool IncludeSystemObjects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to collect row count and size statistics for tables.
    /// </summary>
    public bool IncludeDataStatistics { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to include stored procedures in the analysis.
    /// </summary>
    public bool IncludeStoredProcedures { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to include functions in the analysis.
    /// </summary>
    public bool IncludeFunctions { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to include views in the analysis.
    /// </summary>
    public bool IncludeViews { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to include sequences in the analysis.
    /// </summary>
    public bool IncludeSequences { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of objects to analyze (for large databases).
    /// Null means no limit.
    /// </summary>
    public int? MaxObjects { get; set; }
}
