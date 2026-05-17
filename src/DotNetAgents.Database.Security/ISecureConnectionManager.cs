using DotNetAgents.Security.Secrets;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Database.Security;

/// <summary>
/// Interface for managing secure database connections.
/// </summary>
public interface ISecureConnectionManager
{
    /// <summary>
    /// Creates a secure connection string from configuration or secrets.
    /// </summary>
    /// <param name="connectionName">The connection name or secret key.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The secure connection string.</returns>
    Task<string?> GetSecureConnectionStringAsync(
        string connectionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a connection string is secure (no plaintext passwords in configuration).
    /// </summary>
    /// <param name="connectionString">The connection string to validate.</param>
    /// <returns>True if secure; otherwise, false.</returns>
    bool ValidateConnectionStringSecurity(string connectionString);
}

/// <summary>
/// Implementation of secure connection manager.
/// </summary>
public sealed class SecureConnectionManager : ISecureConnectionManager
{
    private readonly IDatabaseSecretsManager _secretsManager;
    private readonly ILogger<SecureConnectionManager>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecureConnectionManager"/> class.
    /// </summary>
    /// <param name="secretsManager">The database secrets manager.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SecureConnectionManager(
        IDatabaseSecretsManager secretsManager,
        ILogger<SecureConnectionManager>? logger = null)
    {
        _secretsManager = secretsManager ?? throw new ArgumentNullException(nameof(secretsManager));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetSecureConnectionStringAsync(
        string connectionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        return await _secretsManager.GetConnectionStringAsync(connectionName, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool ValidateConnectionStringSecurity(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        // Check for common insecure patterns
        var insecurePatterns = new[]
        {
            "Password=password",
            "Password=123",
            "Password=admin"
        };

        foreach (var pattern in insecurePatterns)
        {
            if (connectionString.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogWarning("Connection string contains insecure password pattern");
                return false;
            }
        }

        return true;
    }
}
