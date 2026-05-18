using System.Data.Common;
using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Tools;


namespace DotNetAgents.Tools.Database;

/// <summary>
/// A tool for executing parameterized database queries.
/// Note: This tool requires a database connection factory to be provided.
/// </summary>
public class DatabaseQueryTool : ITool
{
    private readonly Func<DbConnection> _connectionFactory;
    private static readonly System.Text.Json.JsonElement _inputSchema;

    static DatabaseQueryTool()
    {
        _inputSchema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": {
                    ""type"": ""string"",
                    ""description"": ""The SQL query to execute (must use parameterized queries)""
                },
                ""parameters"": {
                    ""type"": ""object"",
                    ""description"": ""Optional query parameters as key-value pairs""
                },
                ""command_type"": {
                    ""type"": ""string"",
                    ""description"": ""Command type: 'Text' or 'StoredProcedure'. Default: 'Text'"",
                    ""enum"": [""Text"", ""StoredProcedure""]
                },
                ""timeout"": {
                    ""type"": ""integer"",
                    ""description"": ""Command timeout in seconds. Default: 30""
                }
            },
            ""required"": [""query""]
        }");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseQueryTool"/> class.
    /// </summary>
    /// <param name="connectionFactory">Factory function that creates database connections.</param>
    public DatabaseQueryTool(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc/>
    public string Name => "database_query";

    /// <inheritdoc/>
    public string Description => "Executes parameterized SQL queries against a database. Supports SELECT, INSERT, UPDATE, DELETE operations with parameter binding for security.";

    /// <inheritdoc/>
    public System.Text.Json.JsonElement InputSchema => _inputSchema;

    /// <inheritdoc/>
    public async Task<ToolResult> ExecuteAsync(
        object input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parameters = ParseInput(input);
        if (!parameters.TryGetValue("query", out var queryObj) || queryObj is not string query)
        {
            return ToolResult.Failure("Missing or invalid 'query' parameter.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Failure("Query cannot be null or empty.");
        }

        // Security check: Ensure query uses parameters (basic check)
        if (!IsParameterizedQuery(query))
        {
            return ToolResult.Failure(
                "Query must use parameterized queries for security. Use @param, :param, or ? placeholders instead of string concatenation.");
        }

        var commandType = System.Data.CommandType.Text;
        if (parameters.TryGetValue("command_type", out var commandTypeObj) && commandTypeObj is string commandTypeStr)
        {
            commandType = commandTypeStr.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase)
                ? System.Data.CommandType.StoredProcedure
                : System.Data.CommandType.Text;
        }

        var timeout = 30;
        if (parameters.TryGetValue("timeout", out var timeoutObj))
        {
            if (timeoutObj is int timeoutInt && timeoutInt > 0)
            {
                timeout = timeoutInt;
            }
            else if (timeoutObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                var timeoutValue = jsonElement.GetInt32();
                if (timeoutValue > 0)
                {
                    timeout = timeoutValue;
                }
            }
        }

        DbConnection? connection = null;
        try
        {
            connection = _connectionFactory();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandType = commandType;
            command.CommandTimeout = timeout;

            // Add parameters
            if (parameters.TryGetValue("parameters", out var paramsObj))
            {
                AddParameters(command, paramsObj);
            }

            var results = new List<Dictionary<string, object>>();
            var rowsAffected = 0;

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (reader.HasRows)
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[columnName] = value ?? DBNull.Value;
                    }
                    results.Add(row);
                }
            }
            else
            {
                // For non-query commands, get rows affected
                rowsAffected = reader.RecordsAffected;
            }

            var output = new Dictionary<string, object>
            {
                ["rows"] = results,
                ["row_count"] = results.Count,
                ["rows_affected"] = rowsAffected
            };

            return ToolResult.Success(
                System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["row_count"] = results.Count,
                    ["rows_affected"] = rowsAffected
                });
        }
        catch (DbException ex)
        {
            return ToolResult.Failure(
                $"Database error: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["query"] = query,
                    ["error_code"] = ex.ErrorCode
                });
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(
                $"Query execution failed: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["query"] = query
                });
        }
        finally
        {
            connection?.Dispose();
        }
    }

    private static bool IsParameterizedQuery(string query)
    {
        // Basic check: look for parameter placeholders
        // This is a simple check - in production, you might want more sophisticated validation
        var upperQuery = query.ToUpperInvariant();

        // Check for common SQL injection patterns
        if (upperQuery.Contains("' OR '1'='1", StringComparison.Ordinal) ||
            upperQuery.Contains("'; DROP", StringComparison.Ordinal) ||
            upperQuery.Contains("UNION SELECT", StringComparison.Ordinal))
        {
            return false;
        }

        // Check if query uses parameter placeholders
        // SQL Server: @param, PostgreSQL: :param or $1, MySQL: ? or @param
        return query.Contains('@') || query.Contains(':') || query.Contains('?');
    }

    private static void AddParameters(DbCommand command, object parametersObj)
    {
        if (parametersObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in jsonElement.EnumerateObject())
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = prop.Name;
                parameter.Value = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString()!,
                    System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => DBNull.Value,
                    _ => prop.Value.ToString()
                };
                command.Parameters.Add(parameter);
            }
        }
        else if (parametersObj is IDictionary<string, object> paramsDict)
        {
            foreach (var kvp in paramsDict)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = kvp.Key;
                parameter.Value = kvp.Value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }
    }

    private static IDictionary<string, object> ParseInput(object input)
    {
        if (input is System.Text.Json.JsonElement jsonElement)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in jsonElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => prop.Value.GetString()!,
                    System.Text.Json.JsonValueKind.Number => prop.Value.GetDouble(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Object => prop.Value,
                    System.Text.Json.JsonValueKind.Array => prop.Value,
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
