using DotNetAgents.Security.Secrets;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Database.Security;

/// <summary>
/// Interface for managing database connection secrets securely.
/// </summary>
public interface IDatabaseSecretsManager
{
    /// <summary>
    /// Gets a database connection string from secrets.
    /// </summary>
    /// <param name="secretKey">The secret key for the connection string.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The connection string, or null if not found.</returns>
    Task<string?> GetConnectionStringAsync(
        string secretKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Masks sensitive information in a connection string for logging.
    /// </summary>
    /// <param name="connectionString">The connection string to mask.</param>
    /// <returns>The masked connection string.</returns>
    string MaskConnectionString(string connectionString);
}

/// <summary>
/// Implementation of database secrets manager using ISecretsProvider.
/// </summary>
public sealed class DatabaseSecretsManager : IDatabaseSecretsManager
{
    private readonly ISecretsProvider _secretsProvider;
    private readonly ILogger<DatabaseSecretsManager>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseSecretsManager"/> class.
    /// </summary>
    /// <param name="secretsProvider">The secrets provider.</param>
    /// <param name="logger">Optional logger instance.</param>
    public DatabaseSecretsManager(
        ISecretsProvider secretsProvider,
        ILogger<DatabaseSecretsManager>? logger = null)
    {
        _secretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetConnectionStringAsync(
        string secretKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        try
        {
            var connectionString = await _secretsProvider.GetSecretAsync(secretKey, cancellationToken).ConfigureAwait(false);
            if (connectionString == null)
            {
                _logger?.LogWarning("Connection string secret not found: {SecretKey}", secretKey);
            }

            return connectionString;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to retrieve connection string secret: {SecretKey}", secretKey);
            throw;
        }
    }

    /// <inheritdoc />
    public string MaskConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        // Mask password and sensitive information
        var masked = System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"(Password|Pwd|PASSWORD|PWD)=[^;]+",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return masked;
    }
}
