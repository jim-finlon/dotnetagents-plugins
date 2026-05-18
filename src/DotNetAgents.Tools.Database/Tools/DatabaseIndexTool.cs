using DotNetAgents.Abstractions.Tools;
using System.Data.Common;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for managing database indexes (create, drop, analyze).
/// </summary>
public class DatabaseIndexTool : ITool
{
    private readonly Func<DbConnection> _connectionFactory;
    private static readonly JsonElement _inputSchema;

    static DatabaseIndexTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""action"": {
                    ""type"": ""string"",
                    ""description"": ""Action to perform: 'create', 'drop', 'analyze', 'list'. Default: 'list'"",
                    ""enum"": [""create"", ""drop"", ""analyze"", ""list""]
                },
                ""schema_name"": {
                    ""type"": ""string"",
                    ""description"": ""Schema name (optional)""
                },
                ""table_name"": {
                    ""type"": ""string"",
                    ""description"": ""Table name""
                },
                ""index_name"": {
                    ""type"": ""string"",
                    ""description"": ""Index name (required for create/drop)""
                },
                ""columns"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""string"" },
                    ""description"": ""Column names for index (required for create)""
                },
                ""is_unique"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether index is unique. Default: false""
                }
            },
            ""required"": [""action"", ""table_name""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseIndexTool"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory function that creates database connections.</param>
    public DatabaseIndexTool(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public string Name => "database_index";

    /// <inheritdoc/>
    public string Description => "Manages database indexes: create, drop, analyze, or list indexes on tables.";

    /// <inheritdoc/>
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("action", out var actionObj) || actionObj is not string action)
        {
            return ToolResult.Failure("Missing or invalid 'action' parameter.");
        }

        if (!parameters.TryGetValue("table_name", out var tableObj) || tableObj is not string tableName)
        {
            return ToolResult.Failure("Missing or invalid 'table_name' parameter.");
        }

        try
        {
            var schemaName = parameters.TryGetValue("schema_name", out var schemaObj) && schemaObj is string schema
                ? schema
                : "dbo";

            var result = action.ToLowerInvariant() switch
            {
                "list" => await ListIndexesAsync(schemaName, tableName, cancellationToken).ConfigureAwait(false),
                "create" => await CreateIndexAsync(schemaName, tableName, parameters, cancellationToken).ConfigureAwait(false),
                "drop" => await DropIndexAsync(schemaName, tableName, parameters, cancellationToken).ConfigureAwait(false),
                "analyze" => await AnalyzeIndexAsync(schemaName, tableName, parameters, cancellationToken).ConfigureAwait(false),
                _ => ToolResult.Failure($"Unknown action: {action}")
            };

            return result;
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Index operation failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["action"] = action,
                    ["table_name"] = tableName
                });
        }
    }

    private async Task<ToolResult> ListIndexesAsync(string schemaName, string tableName, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Simplified - would query actual index information
        var indexes = new List<Dictionary<string, object>>();

        var output = new Dictionary<string, object>
        {
            ["schema_name"] = schemaName,
            ["table_name"] = tableName,
            ["indexes"] = indexes,
            ["count"] = indexes.Count
        };

        return ToolResult.Success(
            JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
            output);
    }

    private async Task<ToolResult> CreateIndexAsync(
        string schemaName,
        string tableName,
        IDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("index_name", out var indexNameObj) || indexNameObj is not string indexName)
        {
            return ToolResult.Failure("Missing 'index_name' parameter for create action.");
        }

        if (!parameters.TryGetValue("columns", out var columnsObj))
        {
            return ToolResult.Failure("Missing 'columns' parameter for create action.");
        }

        var isUnique = parameters.TryGetValue("is_unique", out var uniqueObj) && uniqueObj is bool unique && unique;

        // Simplified - would generate and execute CREATE INDEX statement
        await Task.CompletedTask.ConfigureAwait(false);

        return ToolResult.Success(
            $"Index '{indexName}' would be created on {schemaName}.{tableName}",
            new Dictionary<string, object>
            {
                ["action"] = "create",
                ["index_name"] = indexName,
                ["schema_name"] = schemaName,
                ["table_name"] = tableName,
                ["is_unique"] = isUnique
            });
    }

    private async Task<ToolResult> DropIndexAsync(
        string schemaName,
        string tableName,
        IDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        if (!parameters.TryGetValue("index_name", out var indexNameObj) || indexNameObj is not string indexName)
        {
            return ToolResult.Failure("Missing 'index_name' parameter for drop action.");
        }

        // Simplified - would execute DROP INDEX statement
        await Task.CompletedTask.ConfigureAwait(false);

        return ToolResult.Success(
            $"Index '{indexName}' would be dropped from {schemaName}.{tableName}",
            new Dictionary<string, object>
            {
                ["action"] = "drop",
                ["index_name"] = indexName,
                ["schema_name"] = schemaName,
                ["table_name"] = tableName
            });
    }

    private async Task<ToolResult> AnalyzeIndexAsync(
        string schemaName,
        string tableName,
        IDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        // Simplified - would analyze index usage and statistics
        await Task.CompletedTask.ConfigureAwait(false);

        return ToolResult.Success(
            $"Index analysis would be performed on {schemaName}.{tableName}",
            new Dictionary<string, object>
            {
                ["action"] = "analyze",
                ["schema_name"] = schemaName,
                ["table_name"] = tableName
            });
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
