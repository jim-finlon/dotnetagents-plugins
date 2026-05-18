using DotNetAgents.Abstractions.Tools;
using DotNetAgents.Database.AI;
using DotNetAgents.Database.Abstractions;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for converting stored procedures between database systems using AI.
/// </summary>
public class AIProcedureConverterTool : ITool
{
    private readonly AIProcedureConverter _converter;
    private static readonly JsonElement _inputSchema;

    static AIProcedureConverterTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""schema_name"": {
                    ""type"": ""string"",
                    ""description"": ""Schema name""
                },
                ""procedure_name"": {
                    ""type"": ""string"",
                    ""description"": ""Procedure name""
                },
                ""definition"": {
                    ""type"": ""string"",
                    ""description"": ""Procedure definition (SQL)""
                },
                ""target_database"": {
                    ""type"": ""string"",
                    ""description"": ""Target database system (e.g., 'PostgreSQL', 'SQL Server')""
                }
            },
            ""required"": [""procedure_name"", ""definition"", ""target_database""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProcedureConverterTool"/> class.
    /// </summary>
    /// <param name="converter">The AI procedure converter.</param>
    public AIProcedureConverterTool(AIProcedureConverter converter)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }

    /// <inheritdoc/>
    public string Name => "ai_procedure_converter";

    /// <inheritdoc/>
    public string Description => "Converts stored procedures from one database system to another using AI, handling syntax differences, function mappings, and error handling patterns.";

    /// <inheritdoc/>
    public JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("procedure_name", out var procNameObj) || procNameObj is not string procedureName)
        {
            return ToolResult.Failure("Missing or invalid 'procedure_name' parameter.");
        }

        if (!parameters.TryGetValue("definition", out var defObj) || defObj is not string definition)
        {
            return ToolResult.Failure("Missing or invalid 'definition' parameter.");
        }

        if (!parameters.TryGetValue("target_database", out var targetObj) || targetObj is not string targetDatabase)
        {
            return ToolResult.Failure("Missing or invalid 'target_database' parameter.");
        }

        var schemaName = parameters.TryGetValue("schema_name", out var schemaObj) && schemaObj is string schema
            ? schema
            : "dbo";

        var procedure = new StoredProcedure
        {
            SchemaName = schemaName,
            ProcedureName = procedureName,
            Definition = definition
        };

        try
        {
            var result = await _converter.ConvertAsync(procedure, targetDatabase, cancellationToken).ConfigureAwait(false);

            var output = new Dictionary<string, object>
            {
                ["original_procedure_name"] = result.OriginalProcedureName,
                ["converted_definition"] = result.ConvertedDefinition,
                ["conversion_notes"] = result.ConversionNotes,
                ["confidence_score"] = result.ConfidenceScore,
                ["warnings"] = result.Warnings
            };

            return ToolResult.Success(
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                output);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Procedure conversion failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["procedure_name"] = procedureName,
                    ["target_database"] = targetDatabase
                });
        }
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
