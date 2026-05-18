using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Tools;
using DotNetAgents.Database.Analysis;
using DotNetAgents.Database.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for analyzing database schemas.
/// </summary>
public class DatabaseSchemaTool : ITool
{
    private readonly SchemaAnalyzerFactory _analyzerFactory;
    private readonly ILogger<DatabaseSchemaTool>? _logger;
    private static readonly JsonElement _inputSchema;

    static DatabaseSchemaTool()
    {
        _inputSchema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""connection_string"": {
                    ""type"": ""string"",
                    ""description"": ""The database connection string""
                },
                ""include_system_objects"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to include system objects. Default: false""
                },
                ""include_data_statistics"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to collect row count and size statistics. Default: true""
                },
                ""include_stored_procedures"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to include stored procedures. Default: true""
                },
                ""include_functions"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to include functions. Default: true""
                },
                ""include_views"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to include views. Default: true""
                },
                ""include_sequences"": {
                    ""type"": ""boolean"",
                    ""description"": ""Whether to include sequences. Default: true""
                }
            },
            ""required"": [""connection_string""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseSchemaTool"/> class.
    /// </summary>
    /// <param name="analyzerFactory">The schema analyzer factory.</param>
    /// <param name="logger">Optional logger instance.</param>
    public DatabaseSchemaTool(SchemaAnalyzerFactory analyzerFactory, ILogger<DatabaseSchemaTool>? logger = null)
    {
        _analyzerFactory = analyzerFactory ?? throw new ArgumentNullException(nameof(analyzerFactory));
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "database_schema_analyze";

    /// <inheritdoc/>
    public string Description => "Analyzes a database schema and extracts information about tables, columns, indexes, constraints, views, stored procedures, functions, and sequences.";

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

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return ToolResult.Failure("Connection string cannot be null or empty.");
        }

        try
        {
            var options = new SchemaAnalysisOptions
            {
                IncludeSystemObjects = parameters.TryGetValue("include_system_objects", out var sysObj) && sysObj is bool sysBool && sysBool,
                IncludeDataStatistics = !parameters.TryGetValue("include_data_statistics", out var statsObj) || !(statsObj is bool statsBool) || statsBool,
                IncludeStoredProcedures = !parameters.TryGetValue("include_stored_procedures", out var procObj) || !(procObj is bool procBool) || procBool,
                IncludeFunctions = !parameters.TryGetValue("include_functions", out var funcObj) || !(funcObj is bool funcBool) || funcBool,
                IncludeViews = !parameters.TryGetValue("include_views", out var viewObj) || !(viewObj is bool viewBool) || viewBool,
                IncludeSequences = !parameters.TryGetValue("include_sequences", out var seqObj) || !(seqObj is bool seqBool) || seqBool
            };

            _logger?.LogInformation("Analyzing database schema");

            var analyzer = await _analyzerFactory.GetAnalyzerAsync(connectionString, cancellationToken).ConfigureAwait(false);
            if (analyzer == null)
            {
                return ToolResult.Failure("No compatible schema analyzer found for the connection string. Supported providers: SQL Server, PostgreSQL.");
            }

            var schema = await analyzer.AnalyzeAsync(connectionString, options, cancellationToken).ConfigureAwait(false);
            var statistics = schema.GetStatistics();

            var output = new Dictionary<string, object>
            {
                ["database_name"] = schema.Name,
                ["table_count"] = schema.TableCount,
                ["view_count"] = schema.Views.Count,
                ["stored_procedure_count"] = schema.StoredProcedures.Count,
                ["function_count"] = schema.Functions.Count,
                ["sequence_count"] = schema.Sequences.Count,
                ["total_objects"] = schema.TotalObjectCount,
                ["total_columns"] = statistics.TotalColumnCount,
                ["total_estimated_rows"] = statistics.TotalEstimatedRows,
                ["schema_count"] = statistics.SchemaCount,
                ["analyzed_date"] = schema.AnalyzedDate.ToString("O"),
                ["statistics"] = new Dictionary<string, object>
                {
                    ["tables_with_identity"] = statistics.TablesWithIdentityCount,
                    ["tables_with_foreign_keys"] = statistics.TablesWithForeignKeysCount,
                    ["total_foreign_keys"] = statistics.TotalForeignKeyCount
                }
            };

            return ToolResult.Success(
                JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }),
                new Dictionary<string, object>
                {
                    ["database_name"] = schema.Name,
                    ["object_count"] = schema.TotalObjectCount
                });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Schema analysis failed");
            return ToolResult.Failure(
                $"Schema analysis failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["error_type"] = ex.GetType().Name
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
