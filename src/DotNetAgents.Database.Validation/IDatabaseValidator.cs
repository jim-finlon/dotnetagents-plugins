namespace DotNetAgents.Database.Validation;

/// <summary>
/// Interface for validating database operations and structure.
/// </summary>
public interface IDatabaseValidator
{
    /// <summary>
    /// Validates a database connection and environment before operations.
    /// </summary>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="options">Validation options.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The validation result.</returns>
    Task<ValidationResult> ValidateAsync(
        string connectionString,
        ValidationOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for database validation.
/// </summary>
public sealed class ValidationOptions
{
    /// <summary>
    /// Gets or sets the validation type.
    /// </summary>
    public ValidationType Type { get; set; } = ValidationType.Schema;

    /// <summary>
    /// Gets or sets whether to check permissions.
    /// </summary>
    public bool CheckPermissions { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to check disk space.
    /// </summary>
    public bool CheckDiskSpace { get; set; } = false;
}

/// <summary>
/// Type of validation to perform.
/// </summary>
public enum ValidationType
{
    /// <summary>
    /// Schema validation.
    /// </summary>
    Schema,

    /// <summary>
    /// Connection validation.
    /// </summary>
    Connection,

    /// <summary>
    /// Permissions validation.
    /// </summary>
    Permissions,

    /// <summary>
    /// Data integrity validation.
    /// </summary>
    Integrity
}
