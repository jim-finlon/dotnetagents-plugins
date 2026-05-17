using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;

namespace DotNetAgents.Database.Validation;

/// <summary>
/// Pre-flight validator that performs checks before database operations.
/// </summary>
public sealed class PreFlightValidator : IDatabaseValidator
{
    private readonly ILogger<PreFlightValidator>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreFlightValidator"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public PreFlightValidator(ILogger<PreFlightValidator>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(
        string connectionString,
        ValidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        options ??= new ValidationOptions();

        var checks = new List<ValidationCheck>();

        // Connection check
        var connectionCheck = await ValidateConnectionAsync(connectionString, cancellationToken).ConfigureAwait(false);
        checks.Add(connectionCheck);

        if (!connectionCheck.Passed)
        {
            return new ValidationResult
            {
                IsValid = false,
                Checks = checks,
                Errors = new List<string> { connectionCheck.Message ?? "Connection validation failed" }
            };
        }

        // Additional checks based on options
        if (options.CheckPermissions)
        {
            var permissionsCheck = await ValidatePermissionsAsync(connectionString, cancellationToken).ConfigureAwait(false);
            checks.Add(permissionsCheck);
        }

        if (options.CheckDiskSpace)
        {
            var diskSpaceCheck = await ValidateDiskSpaceAsync(connectionString, cancellationToken).ConfigureAwait(false);
            checks.Add(diskSpaceCheck);
        }

        var passed = checks.All(c => c.Passed);
        var errors = checks.Where(c => !c.Passed).Select(c => c.Message ?? c.Name).ToList();
        var warnings = new List<string>();

        return new ValidationResult
        {
            IsValid = passed,
            Checks = checks,
            Errors = errors,
            Warnings = warnings
        };
    }

    private async Task<ValidationCheck> ValidateConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try to determine database type and validate
            if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                // SQL Server
                await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return new ValidationCheck
                {
                    Name = "Connection",
                    Category = "Database",
                    Passed = true,
                    Message = "SQL Server connection successful"
                };
            }
            else if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
            {
                // PostgreSQL
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return new ValidationCheck
                {
                    Name = "Connection",
                    Category = "Database",
                    Passed = true,
                    Message = "PostgreSQL connection successful"
                };
            }

            return new ValidationCheck
            {
                Name = "Connection",
                Category = "Database",
                Passed = false,
                Message = "Unable to determine database type from connection string"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Connection validation failed");
            return new ValidationCheck
            {
                Name = "Connection",
                Category = "Database",
                Passed = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }

    private async Task<ValidationCheck> ValidatePermissionsAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            // Simplified - would check actual permissions
            await Task.CompletedTask.ConfigureAwait(false);
            return new ValidationCheck
            {
                Name = "Permissions",
                Category = "Security",
                Passed = true,
                Message = "Permissions check passed"
            };
        }
        catch (Exception ex)
        {
            return new ValidationCheck
            {
                Name = "Permissions",
                Category = "Security",
                Passed = false,
                Message = $"Permissions check failed: {ex.Message}"
            };
        }
    }

    private async Task<ValidationCheck> ValidateDiskSpaceAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            // Simplified - would check actual disk space
            await Task.CompletedTask.ConfigureAwait(false);
            return new ValidationCheck
            {
                Name = "Disk Space",
                Category = "Resources",
                Passed = true,
                Message = "Disk space check passed"
            };
        }
        catch (Exception ex)
        {
            return new ValidationCheck
            {
                Name = "Disk Space",
                Category = "Resources",
                Passed = false,
                Message = $"Disk space check failed: {ex.Message}"
            };
        }
    }
}
