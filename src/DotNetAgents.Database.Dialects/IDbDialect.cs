namespace DotNetAgents.Database.Dialects;

/// <summary>
/// Abstraction for database-specific SQL dialect differences.
/// Implementations handle the translation between different SQL dialects.
/// </summary>
public interface IDbDialect
{
    /// <summary>
    /// Gets the database type this dialect handles.
    /// </summary>
    string DatabaseType { get; }

    /// <summary>
    /// Quotes an identifier (table name, column name, etc.) according to the database's rules.
    /// </summary>
    /// <param name="identifier">The identifier to quote.</param>
    /// <returns>The properly quoted identifier.</returns>
    string QuoteIdentifier(string identifier);

    /// <summary>
    /// Gets a parameter placeholder for the given index.
    /// MSSQL uses @p0, @p1, etc. PostgreSQL can use @p0 or $1, $2.
    /// </summary>
    /// <param name="index">Zero-based parameter index.</param>
    /// <returns>The parameter placeholder string.</returns>
    string GetParameterPlaceholder(int index);

    /// <summary>
    /// Gets the parameter name (without placeholder syntax) for the given index.
    /// </summary>
    /// <param name="index">Zero-based parameter index.</param>
    /// <returns>The parameter name.</returns>
    string GetParameterName(int index);

    /// <summary>
    /// Converts a default value expression from source dialect to this dialect.
    /// Handles function translations like GETDATE() → CURRENT_TIMESTAMP.
    /// </summary>
    /// <param name="sourceExpression">The source default value expression.</param>
    /// <param name="dataType">The column data type (for context-aware conversion).</param>
    /// <returns>The converted default value expression.</returns>
    string ConvertDefaultValue(string sourceExpression, string dataType);

    /// <summary>
    /// Converts a function call from source dialect to this dialect.
    /// </summary>
    /// <param name="functionName">The source function name.</param>
    /// <param name="arguments">The function arguments.</param>
    /// <returns>The converted function call.</returns>
    string ConvertFunction(string functionName, string[] arguments);

    /// <summary>
    /// Gets the schema separator for fully qualified names.
    /// MSSQL uses [Schema].[Table], PostgreSQL uses "Schema"."Table".
    /// </summary>
    string SchemaSeparator { get; }

    /// <summary>
    /// Builds a fully qualified table name.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The fully qualified table name.</returns>
    string QualifyTableName(string schemaName, string tableName);

    /// <summary>
    /// Converts a SQL statement from source dialect to this dialect.
    /// Handles common patterns like TOP N → LIMIT N.
    /// </summary>
    /// <param name="sql">The SQL statement to convert.</param>
    /// <returns>The converted SQL statement.</returns>
    string ConvertSql(string sql);

    /// <summary>
    /// Converts a VIEW definition from source dialect to this dialect.
    /// </summary>
    /// <param name="viewName">The view name.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="viewDefinition">The source view definition (SELECT statement).</param>
    /// <returns>The result containing the converted view SQL and conversion metadata.</returns>
    SqlConversionResult ConvertView(string viewName, string schemaName, string viewDefinition);

    /// <summary>
    /// Converts a stored procedure from source dialect to this dialect (typically a function in PostgreSQL).
    /// </summary>
    /// <param name="procedureName">The procedure name.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="procedureDefinition">The source procedure definition.</param>
    /// <param name="parameters">The procedure parameters.</param>
    /// <returns>The result containing the converted function SQL and conversion metadata.</returns>
    SqlConversionResult ConvertProcedure(string procedureName, string schemaName, string procedureDefinition,
        IEnumerable<ProcedureParameter>? parameters = null);

    /// <summary>
    /// Gets SQL statements to execute before migration (e.g., disable triggers).
    /// </summary>
    IEnumerable<string> GetPreMigrationStatements(string schemaName);

    /// <summary>
    /// Gets SQL statements to execute after migration (e.g., VACUUM ANALYZE, re-enable triggers).
    /// </summary>
    IEnumerable<string> GetPostMigrationStatements(string schemaName, IEnumerable<string> tableNames);

    /// <summary>
    /// Converts a source data type to this dialect's equivalent.
    /// </summary>
    /// <param name="sourceType">The source data type.</param>
    /// <param name="maxLength">Optional max length for variable types.</param>
    /// <param name="precision">Optional precision for numeric types.</param>
    /// <param name="scale">Optional scale for numeric types.</param>
    /// <returns>The equivalent data type in this dialect.</returns>
    string ConvertDataType(string sourceType, int? maxLength = null, int? precision = null, int? scale = null);

    /// <summary>
    /// Checks if a specific SQL construct can be converted by rules alone (without AI).
    /// </summary>
    /// <param name="sql">The SQL to check.</param>
    /// <returns>True if the SQL can be fully converted by rules.</returns>
    bool CanConvertWithRules(string sql);

    /// <summary>
    /// Gets a list of source constructs that cannot be automatically converted.
    /// </summary>
    /// <param name="sql">The SQL to analyze.</param>
    /// <returns>List of unsupported constructs found.</returns>
    IEnumerable<UnsupportedConstruct> GetUnsupportedConstructs(string sql);
}

/// <summary>
/// Result of a SQL conversion operation.
/// </summary>
public sealed class SqlConversionResult
{
    /// <summary>
    /// Whether the conversion was successful.
    /// </summary>
    public required bool IsSuccessful { get; init; }

    /// <summary>
    /// The converted SQL.
    /// </summary>
    public required string ConvertedSql { get; init; }

    /// <summary>
    /// Confidence score from 0.0 to 1.0.
    /// </summary>
    public required double Confidence { get; init; }

    /// <summary>
    /// Whether AI assistance is recommended for this conversion.
    /// </summary>
    public required bool RequiresAIReview { get; init; }

    /// <summary>
    /// List of transformations applied.
    /// </summary>
    public List<string> TransformationsApplied { get; init; } = [];

    /// <summary>
    /// List of warnings or issues found.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// List of constructs that couldn't be converted.
    /// </summary>
    public List<UnsupportedConstruct> UnsupportedConstructs { get; init; } = [];
}

/// <summary>
/// Represents an unsupported SQL construct.
/// </summary>
public sealed class UnsupportedConstruct
{
    /// <summary>
    /// The type of construct (e.g., "CURSOR", "GOTO", "OPENROWSET").
    /// </summary>
    public required string ConstructType { get; init; }

    /// <summary>
    /// The actual SQL fragment.
    /// </summary>
    public required string SqlFragment { get; init; }

    /// <summary>
    /// Position in the original SQL.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Suggested manual action or alternative.
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary>
    /// Severity level.
    /// </summary>
    public ConstructSeverity Severity { get; init; } = ConstructSeverity.Error;
}

/// <summary>
/// Severity level for unsupported constructs.
/// </summary>
public enum ConstructSeverity
{
    /// <summary>
    /// Warning - conversion may work but should be reviewed.
    /// </summary>
    Warning,

    /// <summary>
    /// Error - conversion will not work without manual intervention.
    /// </summary>
    Error,

    /// <summary>
    /// Info - informational note about conversion.
    /// </summary>
    Info
}

/// <summary>
/// Represents a stored procedure parameter.
/// </summary>
public sealed class ProcedureParameter
{
    /// <summary>
    /// Parameter name (without @ prefix).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Source data type.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Whether this is an OUTPUT parameter.
    /// </summary>
    public bool IsOutput { get; init; }

    /// <summary>
    /// Default value if any.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Max length for string types.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Precision for numeric types.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// Scale for numeric types.
    /// </summary>
    public int? Scale { get; init; }
}
