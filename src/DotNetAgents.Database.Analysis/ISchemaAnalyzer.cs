using DotNetAgents.Database.Abstractions;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// Provider-agnostic interface for analyzing database schemas.
/// </summary>
public interface ISchemaAnalyzer : IDatabaseSchemaAnalyzer
{
    /// <summary>
    /// Gets the database provider type this analyzer supports.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Validates that a connection string is compatible with this analyzer.
    /// </summary>
    /// <param name="connectionString">The connection string to validate.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>True if the connection string is compatible; otherwise, false.</returns>
    Task<bool> ValidateConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
}
