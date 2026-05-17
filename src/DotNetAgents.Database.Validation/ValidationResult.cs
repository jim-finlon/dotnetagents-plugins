namespace DotNetAgents.Database.Validation;

/// <summary>
/// Result of a database validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the validation checks performed.
    /// </summary>
    public List<ValidationCheck> Checks { get; init; } = [];

    /// <summary>
    /// Gets any errors encountered.
    /// </summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets any warnings.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets the number of passed checks.
    /// </summary>
    public int PassedChecks => Checks.Count(c => c.Passed);

    /// <summary>
    /// Gets the number of failed checks.
    /// </summary>
    public int FailedChecks => Checks.Count(c => !c.Passed);
}

/// <summary>
/// Represents a single validation check.
/// </summary>
public sealed class ValidationCheck
{
    /// <summary>
    /// Gets the check name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether the check passed.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Gets the check message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets the check category.
    /// </summary>
    public string? Category { get; init; }
}
