using System.Text;
using System.Text.RegularExpressions;
using DotNetAgents.Database.Dialects.Transformation;

namespace DotNetAgents.Database.Dialects.Dialects;

/// <summary>
/// PostgreSQL dialect implementation for SQL syntax translation.
/// Handles comprehensive conversion from MSSQL syntax to PostgreSQL.
/// </summary>
public sealed class PostgreSqlDialect : IDbDialect
{
    /// <inheritdoc />
    public string DatabaseType => "PostgreSQL";

    /// <inheritdoc />
    public string SchemaSeparator => ".";

    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // Remove existing MSSQL brackets if present
        identifier = identifier.Trim('[', ']');

        // Remove existing PostgreSQL quotes if present
        identifier = identifier.Trim('"');

        return $"\"{identifier}\"";
    }

    /// <inheritdoc />
    public string GetParameterPlaceholder(int index)
    {
        return $"@p{index}";
    }

    /// <inheritdoc />
    public string GetParameterName(int index)
    {
        return $"p{index}";
    }

    /// <inheritdoc />
    public string QualifyTableName(string schemaName, string tableName)
    {
        return $"{QuoteIdentifier(schemaName)}{SchemaSeparator}{QuoteIdentifier(tableName)}";
    }

    /// <inheritdoc />
    public string ConvertDefaultValue(string sourceExpression, string dataType)
    {
        if (string.IsNullOrWhiteSpace(sourceExpression))
            return sourceExpression;

        var expression = sourceExpression.Trim();

        // Remove parentheses wrapper if present (MSSQL often wraps defaults)
        while (expression.StartsWith('(') && expression.EndsWith(')'))
        {
            expression = expression[1..^1].Trim();
        }

        var upperExpression = expression.ToUpperInvariant();

        // Date/time functions
        if (upperExpression.Contains("GETDATE", StringComparison.OrdinalIgnoreCase) ||
            upperExpression.Contains("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
            return "CURRENT_TIMESTAMP";

        if (upperExpression.Contains("GETUTCDATE", StringComparison.OrdinalIgnoreCase))
            return "CURRENT_TIMESTAMP AT TIME ZONE 'UTC'";

        if (upperExpression.Contains("SYSDATETIME", StringComparison.OrdinalIgnoreCase))
            return "CURRENT_TIMESTAMP";

        // UUID/GUID
        if (upperExpression.Contains("NEWID", StringComparison.OrdinalIgnoreCase) ||
            upperExpression.Contains("NEWSEQUENTIALID", StringComparison.OrdinalIgnoreCase))
            return "gen_random_uuid()";

        // Boolean literals for BIT type
        if (dataType.Contains("BIT", StringComparison.OrdinalIgnoreCase) ||
            dataType.Contains("BOOLEAN", StringComparison.OrdinalIgnoreCase))
        {
            if (expression == "1" || upperExpression == "TRUE" || upperExpression == "'TRUE'")
                return "true";
            if (expression == "0" || upperExpression == "FALSE" || upperExpression == "'FALSE'")
                return "false";
        }

        // Numeric defaults - keep as-is
        if (decimal.TryParse(expression, out _))
            return expression;

        // String defaults - ensure proper quoting
        if (expression.StartsWith('\'') && expression.EndsWith('\''))
            return expression;

        // N'string' to 'string'
        if (expression.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && expression.EndsWith('\''))
            return expression[1..];

        return expression;
    }

    /// <inheritdoc />
    public string ConvertFunction(string functionName, string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(functionName))
            return functionName;

        var upperName = functionName.ToUpperInvariant();

        // Check for transformer in rules
        if (SqlTransformationRules.FunctionTransformers.TryGetValue(upperName, out var transformer))
        {
            return transformer(arguments);
        }

        // Default: keep function as-is
        return arguments.Length == 0
            ? $"{functionName}()"
            : $"{functionName}({string.Join(", ", arguments)})";
    }

    /// <inheritdoc />
    public string ConvertSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var result = sql;

        // Apply all statement patterns
        foreach (var (pattern, replacement, _) in SqlTransformationRules.StatementPatterns)
        {
            result = pattern.Replace(result, replacement);
        }

        // Handle TOP N -> LIMIT N (needs special processing to move LIMIT to end)
        result = ConvertTopToLimit(result);

        // Convert function calls
        result = ConvertFunctionCalls(result);

        return result;
    }

    /// <inheritdoc />
    public SqlConversionResult ConvertView(string viewName, string schemaName, string viewDefinition)
    {
        var transformations = new List<string>();
        var warnings = new List<string>();
        var unsupported = new List<UnsupportedConstruct>();

        // Start with the view definition
        var convertedSql = viewDefinition;

        // Check for unsupported constructs first
        foreach (var (pattern, constructType, suggestion, severity) in SqlTransformationRules.UnsupportedPatterns)
        {
            var matches = pattern.Matches(convertedSql);
            foreach (Match match in matches)
            {
                unsupported.Add(new UnsupportedConstruct
                {
                    ConstructType = constructType,
                    SqlFragment = match.Value,
                    Position = match.Index,
                    Suggestion = suggestion,
                    Severity = severity
                });
            }
        }

        // Apply transformations
        convertedSql = ConvertSql(convertedSql);
        transformations.Add("Applied standard SQL transformations");

        // Clean up the view definition
        convertedSql = CleanViewDefinition(convertedSql);
        transformations.Add("Cleaned view definition");

        // Build the CREATE VIEW statement
        var qualifiedName = QualifyTableName(schemaName, viewName);
        var fullSql = $"CREATE OR REPLACE VIEW {qualifiedName} AS\n{convertedSql}";

        // Calculate confidence
        var confidence = CalculateConfidence(unsupported, warnings);

        return new SqlConversionResult
        {
            IsSuccessful = unsupported.All(u => u.Severity != ConstructSeverity.Error),
            ConvertedSql = fullSql,
            Confidence = confidence,
            RequiresAIReview = confidence < 0.8 || unsupported.Count > 0,
            TransformationsApplied = transformations,
            Warnings = warnings,
            UnsupportedConstructs = unsupported
        };
    }

    /// <inheritdoc />
    public SqlConversionResult ConvertProcedure(string procedureName, string schemaName, string procedureDefinition,
        IEnumerable<ProcedureParameter>? parameters = null)
    {
        var transformations = new List<string>();
        var warnings = new List<string>();
        var unsupported = new List<UnsupportedConstruct>();

        // Check for unsupported constructs
        foreach (var (pattern, constructType, suggestion, severity) in SqlTransformationRules.UnsupportedPatterns)
        {
            var matches = pattern.Matches(procedureDefinition);
            foreach (Match match in matches)
            {
                unsupported.Add(new UnsupportedConstruct
                {
                    ConstructType = constructType,
                    SqlFragment = match.Value,
                    Position = match.Index,
                    Suggestion = suggestion,
                    Severity = severity
                });
            }
        }

        // Extract the procedure body
        var body = ExtractProcedureBody(procedureDefinition);
        transformations.Add("Extracted procedure body");

        // Convert the body
        var convertedBody = ConvertProcedureBody(body);
        transformations.Add("Applied SQL transformations to procedure body");

        // Build parameter list
        var parameterList = parameters?.ToList() ?? ExtractParameters(procedureDefinition);
        var pgParams = BuildPostgresParameters(parameterList);
        transformations.Add($"Converted {parameterList.Count} parameters");

        // Determine return type
        var (returnType, returnsTable) = DetermineReturnType(procedureDefinition, parameterList);

        // Build the CREATE FUNCTION statement
        var qualifiedName = QualifyTableName(schemaName, procedureName);
        var fullSql = BuildPostgresFunction(qualifiedName, pgParams, returnType, returnsTable, convertedBody, parameterList);

        // Calculate confidence
        var confidence = CalculateConfidence(unsupported, warnings);

        // Check if procedure has complex patterns that need AI review
        if (HasComplexPatterns(procedureDefinition))
        {
            warnings.Add("Procedure contains complex patterns that may need manual review");
            confidence *= 0.7;
        }

        return new SqlConversionResult
        {
            IsSuccessful = unsupported.All(u => u.Severity != ConstructSeverity.Error),
            ConvertedSql = fullSql,
            Confidence = confidence,
            RequiresAIReview = confidence < 0.8 || unsupported.Count > 0,
            TransformationsApplied = transformations,
            Warnings = warnings,
            UnsupportedConstructs = unsupported
        };
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPreMigrationStatements(string schemaName)
    {
        yield return $"-- Pre-migration statements for schema {schemaName}";
        yield return $"CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(schemaName)};";

        // Disable triggers on all tables in schema (if any exist)
        yield return $@"
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = '{schemaName}'
    LOOP
        EXECUTE format('ALTER TABLE %I.%I DISABLE TRIGGER ALL', '{schemaName}', r.tablename);
    END LOOP;
END $$;";
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPostMigrationStatements(string schemaName, IEnumerable<string> tableNames)
    {
        yield return $"-- Post-migration statements for schema {schemaName}";

        // Re-enable triggers
        yield return $@"
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = '{schemaName}'
    LOOP
        EXECUTE format('ALTER TABLE %I.%I ENABLE TRIGGER ALL', '{schemaName}', r.tablename);
    END LOOP;
END $$;";

        // VACUUM ANALYZE for better performance
        foreach (var tableName in tableNames)
        {
            yield return $"VACUUM ANALYZE {QualifyTableName(schemaName, tableName)};";
        }
    }

    /// <inheritdoc />
    public string ConvertDataType(string sourceType, int? maxLength = null, int? precision = null, int? scale = null)
    {
        var upperType = sourceType.ToUpperInvariant().Trim();

        // Remove parentheses if present (e.g., "VARCHAR(50)")
        var baseType = upperType;
        if (upperType.Contains('('))
        {
            baseType = upperType[..upperType.IndexOf('(')];
        }

        // Look up in mappings
        if (SqlTransformationRules.DataTypeMappings.TryGetValue(baseType, out var mappedType))
        {
            var result = mappedType;

            // Handle length/precision/scale
            if (maxLength.HasValue && (baseType.Contains("VARCHAR") || baseType.Contains("CHAR")))
            {
                result = $"{result}({maxLength.Value})";
            }
            else if (precision.HasValue && scale.HasValue && (baseType.Contains("DECIMAL") || baseType.Contains("NUMERIC")))
            {
                result = $"{result}({precision.Value},{scale.Value})";
            }
            else if (precision.HasValue && (baseType.Contains("DECIMAL") || baseType.Contains("NUMERIC")))
            {
                result = $"{result}({precision.Value})";
            }

            return result;
        }

        // Default: return as-is with parameters
        var defaultResult = sourceType;
        if (maxLength.HasValue)
        {
            defaultResult = $"{defaultResult}({maxLength.Value})";
        }
        else if (precision.HasValue && scale.HasValue)
        {
            defaultResult = $"{defaultResult}({precision.Value},{scale.Value})";
        }
        else if (precision.HasValue)
        {
            defaultResult = $"{defaultResult}({precision.Value})";
        }

        return defaultResult;
    }

    /// <inheritdoc />
    public bool CanConvertWithRules(string sql)
    {
        // Check if SQL contains unsupported constructs
        foreach (var (pattern, _, _, severity) in SqlTransformationRules.UnsupportedPatterns)
        {
            if (pattern.IsMatch(sql) && severity == ConstructSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public IEnumerable<UnsupportedConstruct> GetUnsupportedConstructs(string sql)
    {
        var unsupported = new List<UnsupportedConstruct>();

        foreach (var (pattern, constructType, suggestion, severity) in SqlTransformationRules.UnsupportedPatterns)
        {
            var matches = pattern.Matches(sql);
            foreach (Match match in matches)
            {
                unsupported.Add(new UnsupportedConstruct
                {
                    ConstructType = constructType,
                    SqlFragment = match.Value,
                    Position = match.Index,
                    Suggestion = suggestion,
                    Severity = severity
                });
            }
        }

        return unsupported;
    }

    #region Helper Methods

    private static string ConvertTopToLimit(string sql)
    {
        // Pattern: SELECT TOP N ... -> SELECT ... LIMIT N
        var topPattern = new Regex(@"\bSELECT\s+TOP\s+(\d+)\s+", RegexOptions.IgnoreCase);
        var match = topPattern.Match(sql);

        if (match.Success)
        {
            var limitValue = match.Groups[1].Value;
            sql = topPattern.Replace(sql, "SELECT ", 1);

            // Find the end of the SELECT statement (before ORDER BY or at end)
            var orderByIndex = sql.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
            if (orderByIndex > 0)
            {
                sql = sql.Insert(orderByIndex, $" LIMIT {limitValue}");
            }
            else
            {
                sql = sql.TrimEnd(';', ' ') + $" LIMIT {limitValue}";
            }
        }

        return sql;
    }

    private static string ConvertFunctionCalls(string sql)
    {
        // This is a simplified version - in production, you'd want a proper SQL parser
        // For now, we'll handle common patterns

        foreach (var (funcName, transformer) in SqlTransformationRules.FunctionTransformers)
        {
            // Simple pattern matching for function calls
            var pattern = new Regex($@"\b{funcName}\s*\(", RegexOptions.IgnoreCase);
            if (pattern.IsMatch(sql))
            {
                // This is a simplified replacement - full implementation would parse arguments
                // For now, we'll leave it to the ConvertFunction method to handle
            }
        }

        return sql;
    }

    private static string CleanViewDefinition(string viewDefinition)
    {
        // Remove trailing semicolons and whitespace
        return viewDefinition.TrimEnd(';', ' ', '\r', '\n');
    }

    private static string ExtractProcedureBody(string procedureDefinition)
    {
        // Extract the body between AS/BEGIN and END
        var beginIndex = procedureDefinition.IndexOf("BEGIN", StringComparison.OrdinalIgnoreCase);
        var asIndex = procedureDefinition.IndexOf("AS", StringComparison.OrdinalIgnoreCase);

        var startIndex = beginIndex > 0 ? beginIndex + 5 : (asIndex > 0 ? asIndex + 2 : 0);
        var endIndex = procedureDefinition.LastIndexOf("END", StringComparison.OrdinalIgnoreCase);

        if (endIndex > startIndex)
        {
            return procedureDefinition[startIndex..endIndex].Trim();
        }

        return procedureDefinition;
    }

    private static string ConvertProcedureBody(string body)
    {
        // Apply SQL transformations to procedure body
        var dialect = new PostgreSqlDialect();
        return dialect.ConvertSql(body);
    }

    private static List<ProcedureParameter> ExtractParameters(string procedureDefinition)
    {
        // Simplified parameter extraction - full implementation would parse CREATE PROCEDURE syntax
        var parameters = new List<ProcedureParameter>();

        // This is a placeholder - full implementation would parse the procedure definition
        // to extract parameter declarations

        return parameters;
    }

    private static string BuildPostgresParameters(List<ProcedureParameter> parameters)
    {
        if (parameters.Count == 0)
            return string.Empty;

        var paramStrings = parameters.Select(p =>
        {
            var pgType = new PostgreSqlDialect().ConvertDataType(p.DataType, p.MaxLength, p.Precision, p.Scale);
            var defaultValue = p.DefaultValue != null ? $" DEFAULT {p.DefaultValue}" : "";
            return $"{p.Name} {pgType}{defaultValue}";
        });

        return string.Join(", ", paramStrings);
    }

    private static (string ReturnType, bool ReturnsTable) DetermineReturnType(string procedureDefinition, List<ProcedureParameter> parameters)
    {
        // Check if procedure has OUTPUT parameters (would become RETURNS TABLE)
        var hasOutputParams = parameters.Any(p => p.IsOutput);

        if (hasOutputParams)
        {
            return ("TABLE", true);
        }

        // Default: void function
        return ("void", false);
    }

    private static string BuildPostgresFunction(string qualifiedName, string parameters, string returnType, bool returnsTable,
        string body, List<ProcedureParameter> parameterList)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"CREATE OR REPLACE FUNCTION {qualifiedName}");

        if (parameterList.Count > 0)
        {
            sb.AppendLine($"({parameters})");
        }

        if (returnsTable)
        {
            sb.AppendLine("RETURNS TABLE");
        }
        else
        {
            sb.AppendLine($"RETURNS {returnType}");
        }

        sb.AppendLine("LANGUAGE plpgsql");
        sb.AppendLine("AS $$");
        sb.AppendLine("BEGIN");
        sb.AppendLine(body);
        sb.AppendLine("END;");
        sb.AppendLine("$$;");

        return sb.ToString();
    }

    private static bool HasComplexPatterns(string sql)
    {
        // Check for complex patterns that might need AI review
        var complexPatterns = new[]
        {
            @"\bCURSOR\b",
            @"\bGOTO\b",
            @"\bEXEC\s+@",
            @"\bOPENROWSET\b"
        };

        foreach (var pattern in complexPatterns)
        {
            if (Regex.IsMatch(sql, pattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static double CalculateConfidence(List<UnsupportedConstruct> unsupported, List<string> warnings)
    {
        var confidence = 1.0;

        // Reduce confidence for errors
        foreach (var construct in unsupported)
        {
            if (construct.Severity == ConstructSeverity.Error)
            {
                confidence -= 0.3;
            }
            else if (construct.Severity == ConstructSeverity.Warning)
            {
                confidence -= 0.1;
            }
        }

        // Reduce confidence for warnings
        confidence -= warnings.Count * 0.05;

        return Math.Max(0.0, Math.Min(1.0, confidence));
    }

    #endregion
}
