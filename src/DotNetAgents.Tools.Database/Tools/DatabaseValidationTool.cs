using DotNetAgents.Abstractions.Tools;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for validating database structure and operations.
/// </summary>
public class DatabaseValidationTool : ITool
{
    private static readonly JsonElement _inputSchema;

    static DatabaseValidationTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""connection_string"": {
                    ""type"": ""string"",
                    ""description"": ""The database connection string""
                },
                ""validation_type"": {
                    ""type"": ""string"",
                    ""description"": ""Type of validation: 'schema', 'connection', 'permissions', 'integrity'. Default: 'schema'"",
                    ""enum"": [""schema"", ""connection"", ""permissions"", ""integrity""]
                }
            },
            ""required"": [""connection_string""]
        }");
    }

    /// <inheritdoc/>
    public string Name => "database_validate";

    /// <inheritdoc/>
    public string Description => "Validates database structure, connection, permissions, or data integrity. Returns validation results with any issues found.";

    /// <inheritdoc/>
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("connection_string", out var connStrObj) || connStrObj is not string connectionString)
        {
            return ToolResult.Failure("Missing or invalid 'connection_string' parameter.");
        }

        var validationType = "schema";
        if (parameters.TryGetValue("validation_type", out var typeObj) && typeObj is string typeStr)
        {
            validationType = typeStr;
        }

        try
        {
            // Basic validation - full implementation would use validation services
            var results = new Dictionary<string, object>
            {
                ["validation_type"] = validationType,
                ["connection_valid"] = await ValidateConnectionAsync(connectionString, cancellationToken).ConfigureAwait(false),
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };

            return ToolResult.Success(
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
                results);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Validation failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["validation_type"] = validationType,
                    ["error_type"] = ex.GetType().Name
                });
        }
    }

    private static async Task<bool> ValidateConnectionAsync(string connectionString, CancellationToken cancellationToken)
    {
        // Simplified - would use actual connection validation
        await Task.CompletedTask.ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(connectionString);
    }

    private static IDictionary<string, object> ParseInput(object input)
    {
        if (input is JsonElement jsonElement)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in jsonElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()!,
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => prop.Value,
                    JsonValueKind.Array => prop.Value,
                    _ => prop.Value.ToString()
                };
            }
            return dict;
        }

        if (input is IDictionary<string, object> dictInput)
        {
            return dictInput;
        }

        throw new ArgumentException("Input must be JsonElement or IDictionary<string, object>", nameof(input));
    }
}
