using Microsoft.Extensions.Logging;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// Factory for creating appropriate schema analyzers based on connection string.
/// </summary>
public sealed class SchemaAnalyzerFactory
{
    private readonly IEnumerable<ISchemaAnalyzer> _analyzers;
    private readonly ILogger<SchemaAnalyzerFactory>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaAnalyzerFactory"/> class.
    /// </summary>
    /// <param name="analyzers">The available schema analyzers.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SchemaAnalyzerFactory(
        IEnumerable<ISchemaAnalyzer> analyzers,
        ILogger<SchemaAnalyzerFactory>? logger = null)
    {
        _analyzers = analyzers ?? throw new ArgumentNullException(nameof(analyzers));
        _logger = logger;
    }

    /// <summary>
    /// Gets the appropriate schema analyzer for the given connection string.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The appropriate schema analyzer, or null if none match.</returns>
    public async Task<ISchemaAnalyzer?> GetAnalyzerAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        foreach (var analyzer in _analyzers)
        {
            if (await analyzer.ValidateConnectionAsync(connectionString, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogDebug("Selected analyzer: {ProviderType}", analyzer.ProviderType);
                return analyzer;
            }
        }

        _logger?.LogWarning("No compatible schema analyzer found for connection string");
        return null;
    }
}
