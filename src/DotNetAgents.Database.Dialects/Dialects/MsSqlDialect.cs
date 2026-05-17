namespace DotNetAgents.Database.Dialects.Dialects;

/// <summary>
/// MSSQL dialect implementation.
/// This is primarily used for generating SQL that runs against MSSQL (source database).
/// Conversion methods return the source SQL unchanged as MSSQL is the source dialect.
/// </summary>
public sealed class MsSqlDialect : IDbDialect
{
    /// <inheritdoc />
    public string DatabaseType => "MSSQL";

    /// <inheritdoc />
    public string SchemaSeparator => ".";

    /// <inheritdoc />
    public string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return identifier;

        // Remove existing quotes if present
        identifier = identifier.Trim('[', ']', '"');

        return $"[{identifier}]";
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
        // For MSSQL, no conversion needed - it's the source
        return sourceExpression;
    }

    /// <inheritdoc />
    public string ConvertFunction(string functionName, string[] arguments)
    {
        // For MSSQL, no conversion needed - it's the source
        if (arguments.Length == 0)
            return $"{functionName}()";

        return $"{functionName}({string.Join(", ", arguments)})";
    }

    /// <inheritdoc />
    public string ConvertSql(string sql)
    {
        // For MSSQL, no conversion needed - it's the source
        return sql;
    }

    /// <inheritdoc />
    public SqlConversionResult ConvertView(string viewName, string schemaName, string viewDefinition)
    {
        // MSSQL is the source dialect - views don't need conversion
        return new SqlConversionResult
        {
            IsSuccessful = true,
            ConvertedSql = viewDefinition,
            Confidence = 1.0,
            RequiresAIReview = false,
            TransformationsApplied = ["No conversion needed - MSSQL is source dialect"]
        };
    }

    /// <inheritdoc />
    public SqlConversionResult ConvertProcedure(string procedureName, string schemaName, string procedureDefinition,
        IEnumerable<ProcedureParameter>? parameters = null)
    {
        // MSSQL is the source dialect - procedures don't need conversion
        return new SqlConversionResult
        {
            IsSuccessful = true,
            ConvertedSql = procedureDefinition,
            Confidence = 1.0,
            RequiresAIReview = false,
            TransformationsApplied = ["No conversion needed - MSSQL is source dialect"]
        };
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPreMigrationStatements(string schemaName)
    {
        yield return $"-- Pre-migration statements for schema {schemaName}";
        yield return $"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}') EXEC('CREATE SCHEMA [{schemaName}]');";
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPostMigrationStatements(string schemaName, IEnumerable<string> tableNames)
    {
        yield return $"-- Post-migration statements for schema {schemaName}";
        // MSSQL doesn't typically need post-migration statements
    }

    /// <inheritdoc />
    public string ConvertDataType(string sourceType, int? maxLength = null, int? precision = null, int? scale = null)
    {
        // For MSSQL, no conversion needed - it's the source
        var result = sourceType;

        if (maxLength.HasValue && (sourceType.Contains("VARCHAR", StringComparison.OrdinalIgnoreCase) ||
                                   sourceType.Contains("NVARCHAR", StringComparison.OrdinalIgnoreCase) ||
                                   sourceType.Contains("CHAR", StringComparison.OrdinalIgnoreCase) ||
                                   sourceType.Contains("NCHAR", StringComparison.OrdinalIgnoreCase)))
        {
            result = $"{result}({maxLength.Value})";
        }

        if (precision.HasValue && scale.HasValue)
        {
            result = $"{result}({precision.Value},{scale.Value})";
        }
        else if (precision.HasValue)
        {
            result = $"{result}({precision.Value})";
        }

        return result;
    }

    /// <inheritdoc />
    public bool CanConvertWithRules(string sql)
    {
        // MSSQL is the source, so all SQL is already valid MSSQL
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<UnsupportedConstruct> GetUnsupportedConstructs(string sql)
    {
        // MSSQL is the source dialect, so no unsupported constructs
        return Array.Empty<UnsupportedConstruct>();
    }
}
