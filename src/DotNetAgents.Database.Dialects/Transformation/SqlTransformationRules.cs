using System.Text.RegularExpressions;

namespace DotNetAgents.Database.Dialects.Transformation;

/// <summary>
/// Comprehensive SQL transformation rules for MSSQL to PostgreSQL conversion.
/// These rules handle the majority of common patterns without requiring AI.
/// Includes 100+ function mappings, statement patterns, and unsupported construct detection.
/// </summary>
public static class SqlTransformationRules
{
    #region Data Type Mappings

    /// <summary>
    /// Comprehensive MSSQL to PostgreSQL data type mappings.
    /// </summary>
    public static readonly Dictionary<string, string> DataTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Exact numerics
        ["BIGINT"] = "BIGINT",
        ["INT"] = "INTEGER",
        ["INTEGER"] = "INTEGER",
        ["SMALLINT"] = "SMALLINT",
        ["TINYINT"] = "SMALLINT", // PostgreSQL doesn't have TINYINT
        ["BIT"] = "BOOLEAN",
        ["DECIMAL"] = "NUMERIC",
        ["NUMERIC"] = "NUMERIC",
        ["MONEY"] = "NUMERIC(19,4)",
        ["SMALLMONEY"] = "NUMERIC(10,4)",

        // Approximate numerics
        ["FLOAT"] = "DOUBLE PRECISION",
        ["REAL"] = "REAL",

        // Date and time
        ["DATE"] = "DATE",
        ["DATETIME"] = "TIMESTAMP",
        ["DATETIME2"] = "TIMESTAMP",
        ["SMALLDATETIME"] = "TIMESTAMP",
        ["TIME"] = "TIME",
        ["DATETIMEOFFSET"] = "TIMESTAMP WITH TIME ZONE",

        // Character strings
        ["CHAR"] = "CHAR",
        ["VARCHAR"] = "VARCHAR",
        ["TEXT"] = "TEXT",
        ["NCHAR"] = "CHAR", // PostgreSQL uses UTF-8 by default
        ["NVARCHAR"] = "VARCHAR",
        ["NTEXT"] = "TEXT",

        // Binary strings
        ["BINARY"] = "BYTEA",
        ["VARBINARY"] = "BYTEA",
        ["IMAGE"] = "BYTEA",

        // Other data types
        ["UNIQUEIDENTIFIER"] = "UUID",
        ["XML"] = "XML",
        ["SQL_VARIANT"] = "TEXT", // No direct equivalent
        ["HIERARCHYID"] = "TEXT", // No direct equivalent, store as string
        ["GEOGRAPHY"] = "GEOGRAPHY", // Requires PostGIS
        ["GEOMETRY"] = "GEOMETRY", // Requires PostGIS
        ["ROWVERSION"] = "BYTEA",
        ["TIMESTAMP"] = "BYTEA", // MSSQL TIMESTAMP is not a datetime!
    };

    #endregion

    #region Function Mappings

    /// <summary>
    /// Comprehensive function mappings from MSSQL to PostgreSQL.
    /// Over 100 function transformers covering date/time, string, math, aggregates, JSON, and system functions.
    /// </summary>
    public static readonly Dictionary<string, Func<string[], string>> FunctionTransformers = new(StringComparer.OrdinalIgnoreCase)
    {
        // ─── Date/Time functions ───────────────────────────────────────────────────
        ["GETDATE"] = _ => "CURRENT_TIMESTAMP",
        ["GETUTCDATE"] = _ => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')",
        ["SYSDATETIME"] = _ => "CURRENT_TIMESTAMP",
        ["SYSUTCDATETIME"] = _ => "(CURRENT_TIMESTAMP AT TIME ZONE 'UTC')",
        ["SYSDATETIMEOFFSET"] = _ => "CURRENT_TIMESTAMP",
        ["CURRENT_TIMESTAMP"] = _ => "CURRENT_TIMESTAMP",

        ["DATEADD"] = args => ConvertDateAdd(args),
        ["DATEDIFF"] = args => ConvertDateDiff(args),
        ["DATEDIFF_BIG"] = args => ConvertDateDiff(args),
        ["DATEPART"] = args => ConvertDatePart(args),
        ["DATENAME"] = args => ConvertDateName(args),
        ["EOMONTH"] = args => ConvertEoMonth(args),
        ["DATEFROMPARTS"] = args => $"MAKE_DATE({string.Join(", ", args)})",
        ["ISDATE"] = args => $"(CASE WHEN {args[0]}::TEXT ~ '^\\d{{4}}-\\d{{2}}-\\d{{2}}' THEN 1 ELSE 0 END)",

        ["DAY"] = args => $"EXTRACT(DAY FROM {args[0]})::INTEGER",
        ["MONTH"] = args => $"EXTRACT(MONTH FROM {args[0]})::INTEGER",
        ["YEAR"] = args => $"EXTRACT(YEAR FROM {args[0]})::INTEGER",
        ["HOUR"] = args => $"EXTRACT(HOUR FROM {args[0]})::INTEGER",
        ["MINUTE"] = args => $"EXTRACT(MINUTE FROM {args[0]})::INTEGER",
        ["SECOND"] = args => $"EXTRACT(SECOND FROM {args[0]})::INTEGER",

        // ─── String functions ──────────────────────────────────────────────────────
        ["LEN"] = args => $"LENGTH({args[0]})",
        ["DATALENGTH"] = args => $"OCTET_LENGTH({args[0]}::TEXT)",
        ["CHARINDEX"] = args => args.Length >= 3
            ? $"(POSITION({args[0]} IN SUBSTRING({args[1]} FROM {args[2]})) + {args[2]} - 1)"
            : $"POSITION({args[0]} IN {args[1]})",
        ["PATINDEX"] = args => ConvertPatIndex(args),
        ["SUBSTRING"] = args => $"SUBSTRING({args[0]} FROM {args[1]} FOR {(args.Length > 2 ? args[2] : "2147483647")})",
        ["LEFT"] = args => $"LEFT({args[0]}, {args[1]})",
        ["RIGHT"] = args => $"RIGHT({args[0]}, {args[1]})",
        ["LTRIM"] = args => $"LTRIM({args[0]})",
        ["RTRIM"] = args => $"RTRIM({args[0]})",
        ["TRIM"] = args => $"TRIM({args[0]})",
        ["UPPER"] = args => $"UPPER({args[0]})",
        ["LOWER"] = args => $"LOWER({args[0]})",
        ["REPLACE"] = args => $"REPLACE({args[0]}, {args[1]}, {args[2]})",
        ["REPLICATE"] = args => $"REPEAT({args[0]}, {args[1]})",
        ["REVERSE"] = args => $"REVERSE({args[0]})",
        ["SPACE"] = args => $"REPEAT(' ', {args[0]})",
        ["STR"] = args => ConvertStr(args),
        ["STUFF"] = args => $"OVERLAY({args[0]} PLACING {args[3]} FROM {args[1]} FOR {args[2]})",
        ["CONCAT"] = args => $"CONCAT({string.Join(", ", args)})",
        ["CONCAT_WS"] = args => $"CONCAT_WS({string.Join(", ", args)})",
        ["STRING_AGG"] = args => args.Length >= 2
            ? $"STRING_AGG({args[0]}, {args[1]})"
            : $"STRING_AGG({args[0]}, ',')",
        ["FORMAT"] = args => ConvertFormat(args),
        ["QUOTENAME"] = args => $"QUOTE_IDENT({args[0]})",
        ["UNICODE"] = args => $"ASCII({args[0]})",
        ["NCHAR"] = args => $"CHR({args[0]})",
        ["CHAR"] = args => $"CHR({args[0]})",
        ["ASCII"] = args => $"ASCII({args[0]})",
        ["SOUNDEX"] = args => $"SOUNDEX({args[0]})", // Requires fuzzystrmatch extension

        // ─── NULL handling ─────────────────────────────────────────────────────────
        ["ISNULL"] = args => $"COALESCE({args[0]}, {args[1]})",
        ["COALESCE"] = args => $"COALESCE({string.Join(", ", args)})",
        ["NULLIF"] = args => $"NULLIF({args[0]}, {args[1]})",
        ["IIF"] = args => $"(CASE WHEN {args[0]} THEN {args[1]} ELSE {args[2]} END)",

        // ─── Type conversion ───────────────────────────────────────────────────────
        ["CAST"] = args => ConvertCast(args),
        ["CONVERT"] = args => ConvertConvert(args),
        ["TRY_CAST"] = args => ConvertTryCast(args),
        ["TRY_CONVERT"] = args => ConvertTryConvert(args),
        ["PARSE"] = args => ConvertParse(args),
        ["TRY_PARSE"] = args => ConvertTryParse(args),

        // ─── Math functions ────────────────────────────────────────────────────────
        ["ABS"] = args => $"ABS({args[0]})",
        ["CEILING"] = args => $"CEILING({args[0]})",
        ["FLOOR"] = args => $"FLOOR({args[0]})",
        ["ROUND"] = args => args.Length >= 2 ? $"ROUND({args[0]}, {args[1]})" : $"ROUND({args[0]})",
        ["POWER"] = args => $"POWER({args[0]}, {args[1]})",
        ["SQRT"] = args => $"SQRT({args[0]})",
        ["SQUARE"] = args => $"POWER({args[0]}, 2)",
        ["EXP"] = args => $"EXP({args[0]})",
        ["LOG"] = args => args.Length >= 2 ? $"LOG({args[1]}, {args[0]})" : $"LN({args[0]})",
        ["LOG10"] = args => $"LOG({args[0]})",
        ["SIGN"] = args => $"SIGN({args[0]})",
        ["RAND"] = args => args.Length > 0 ? $"SETSEED({args[0]}); RANDOM()" : "RANDOM()",
        ["RANDOM"] = _ => "RANDOM()",
        ["PI"] = _ => "PI()",
        ["DEGREES"] = args => $"DEGREES({args[0]})",
        ["RADIANS"] = args => $"RADIANS({args[0]})",
        ["SIN"] = args => $"SIN({args[0]})",
        ["COS"] = args => $"COS({args[0]})",
        ["TAN"] = args => $"TAN({args[0]})",
        ["ASIN"] = args => $"ASIN({args[0]})",
        ["ACOS"] = args => $"ACOS({args[0]})",
        ["ATAN"] = args => $"ATAN({args[0]})",
        ["ATN2"] = args => $"ATAN2({args[0]}, {args[1]})",
        ["COT"] = args => $"COT({args[0]})",

        // ─── Aggregate functions ───────────────────────────────────────────────────
        ["COUNT"] = args => $"COUNT({string.Join(", ", args)})",
        ["COUNT_BIG"] = args => $"COUNT({string.Join(", ", args)})",
        ["SUM"] = args => $"SUM({args[0]})",
        ["AVG"] = args => $"AVG({args[0]})",
        ["MIN"] = args => $"MIN({args[0]})",
        ["MAX"] = args => $"MAX({args[0]})",
        ["STDEV"] = args => $"STDDEV({args[0]})",
        ["STDEVP"] = args => $"STDDEV_POP({args[0]})",
        ["VAR"] = args => $"VARIANCE({args[0]})",
        ["VARP"] = args => $"VAR_POP({args[0]})",
        ["CHECKSUM_AGG"] = args => $"SUM(HASHTEXT({args[0]}::TEXT))",

        // ─── UUID/GUID ─────────────────────────────────────────────────────────────
        ["NEWID"] = _ => "gen_random_uuid()",
        ["NEWSEQUENTIALID"] = _ => "gen_random_uuid()",

        // ─── System functions ──────────────────────────────────────────────────────
        ["SCOPE_IDENTITY"] = _ => "LASTVAL()",
        ["@@IDENTITY"] = _ => "LASTVAL()",
        ["@@ROWCOUNT"] = _ => "ROW_COUNT", // In PL/pgSQL context
        ["IDENT_CURRENT"] = args => $"CURRVAL(pg_get_serial_sequence({args[0]}, 'id'))",
        ["OBJECT_ID"] = args => $"TO_REGCLASS({args[0]})::OID",
        ["OBJECT_NAME"] = args => $"(SELECT relname FROM pg_class WHERE oid = {args[0]})",
        ["DB_NAME"] = _ => "CURRENT_DATABASE()",
        ["DB_ID"] = args => args.Length > 0
            ? $"(SELECT oid FROM pg_database WHERE datname = {args[0]})"
            : "(SELECT oid FROM pg_database WHERE datname = CURRENT_DATABASE())",
        ["SCHEMA_NAME"] = _ => "CURRENT_SCHEMA()",
        ["SCHEMA_ID"] = args => args.Length > 0
            ? $"(SELECT oid FROM pg_namespace WHERE nspname = {args[0]})"
            : "(SELECT oid FROM pg_namespace WHERE nspname = CURRENT_SCHEMA())",
        ["USER_NAME"] = _ => "CURRENT_USER",
        ["SUSER_NAME"] = _ => "CURRENT_USER",
        ["SUSER_SNAME"] = _ => "CURRENT_USER",
        ["SYSTEM_USER"] = _ => "CURRENT_USER",
        ["SESSION_USER"] = _ => "SESSION_USER",
        ["HOST_NAME"] = _ => "INET_CLIENT_ADDR()::TEXT",
        ["APP_NAME"] = _ => "CURRENT_SETTING('application_name')",

        // ─── JSON functions (SQL Server 2016+) ─────────────────────────────────────
        ["JSON_VALUE"] = args => $"({args[0]})::jsonb #>> {ConvertJsonPath(args[1])}",
        ["JSON_QUERY"] = args => $"({args[0]})::jsonb #> {ConvertJsonPath(args[1])}",
        ["ISJSON"] = args => $"(CASE WHEN {args[0]}::jsonb IS NOT NULL THEN 1 ELSE 0 END)",
        ["JSON_MODIFY"] = args => ConvertJsonModify(args),
    };

    #endregion

    #region Statement Patterns

    /// <summary>
    /// Regex patterns for SQL statement transformations.
    /// Applied in order for comprehensive MSSQL to PostgreSQL statement-level conversion.
    /// </summary>
    public static readonly List<(Regex Pattern, string Replacement, string Description)> StatementPatterns =
    [
        // SELECT TOP N -> LIMIT N (handled specially in ConvertTopToLimit, but pattern needed for detection)
        (new Regex(@"\bSELECT\s+TOP\s+\(?\s*(\d+)\s*\)?\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "SELECT ", "TOP to LIMIT"),

        // Square brackets to double quotes for identifiers
        (new Regex(@"\[([^\]]+)\]", RegexOptions.Compiled),
         "\"$1\"", "Bracket to quote"),

        // NOLOCK and other single table hints
        (new Regex(@"\s+WITH\s*\(\s*(NOLOCK|READUNCOMMITTED|READCOMMITTED|REPEATABLEREAD|SERIALIZABLE|ROWLOCK|PAGELOCK|TABLOCK|TABLOCKX|UPDLOCK|XLOCK|HOLDLOCK)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "", "Remove table hints"),

        // Multiple table hints
        (new Regex(@"\s+WITH\s*\(\s*(NOLOCK|READUNCOMMITTED|READCOMMITTED|REPEATABLEREAD|SERIALIZABLE|ROWLOCK|PAGELOCK|TABLOCK|TABLOCKX|UPDLOCK|XLOCK|HOLDLOCK)\s*,\s*(NOLOCK|READUNCOMMITTED|READCOMMITTED|REPEATABLEREAD|SERIALIZABLE|ROWLOCK|PAGELOCK|TABLOCK|TABLOCKX|UPDLOCK|XLOCK|HOLDLOCK)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "", "Remove multiple table hints"),

        // String concatenation with + to ||
        (new Regex(@"(?<str1>'[^']*')\s*\+\s*(?<str2>'[^']*')", RegexOptions.Compiled),
         "${str1} || ${str2}", "String concat"),

        // N'string' to 'string'
        (new Regex(@"\bN'", RegexOptions.Compiled),
         "'", "Unicode string prefix"),

        // BIT comparisons
        (new Regex(@"=\s*1\b", RegexOptions.Compiled),
         "= TRUE", "Bit to boolean true"),
        (new Regex(@"=\s*0\b", RegexOptions.Compiled),
         "= FALSE", "Bit to boolean false"),

        // PRINT statement to RAISE NOTICE
        (new Regex(@"^\s*PRINT\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
         "RAISE NOTICE '%', $1;", "PRINT to RAISE NOTICE"),

        // SET NOCOUNT ON/OFF (not needed in PostgreSQL)
        (new Regex(@"^\s*SET\s+NOCOUNT\s+(ON|OFF)\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
         "", "Remove SET NOCOUNT"),

        // SET ANSI_NULLS, SET QUOTED_IDENTIFIER, and other session options (not needed)
        (new Regex(@"^\s*SET\s+(ANSI_NULLS|QUOTED_IDENTIFIER|ANSI_PADDING|ANSI_WARNINGS|ARITHABORT|CONCAT_NULL_YIELDS_NULL|NUMERIC_ROUNDABORT)\s+(ON|OFF)\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
         "", "Remove SET options"),

        // EXEC/EXECUTE procedure -> SELECT * FROM function()
        (new Regex(@"\bEXEC(?:UTE)?\s+(\w+)\.(\w+)\s*;", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "SELECT * FROM \"$1\".\"$2\"();", "EXEC to SELECT"),

        // @@ERROR to SQLSTATE
        (new Regex(@"@@ERROR", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "SQLSTATE", "@@ERROR to SQLSTATE"),

        // @@IDENTITY to RETURNING
        (new Regex(@"@@IDENTITY", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "LASTVAL()", "@@IDENTITY to LASTVAL"),

        // @@ROWCOUNT
        (new Regex(@"@@ROWCOUNT", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "ROW_COUNT", "@@ROWCOUNT to ROW_COUNT"),

        // @@TRANCOUNT
        (new Regex(@"@@TRANCOUNT", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "(SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active' AND xact_start IS NOT NULL AND pid = pg_backend_pid())", "@@TRANCOUNT"),

        // RAISERROR to RAISE
        (new Regex(@"\bRAISERROR\s*\(\s*'([^']+)'\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "RAISE EXCEPTION '$1'", "RAISERROR to RAISE"),

        // IF EXISTS with subquery
        (new Regex(@"\bIF\s+EXISTS\s*\(\s*SELECT", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "IF EXISTS (SELECT", "IF EXISTS"),

        // BEGIN TRANSACTION -> BEGIN
        (new Regex(@"\bBEGIN\s+TRAN(?:SACTION)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "BEGIN", "BEGIN TRANSACTION"),

        // COMMIT TRANSACTION -> COMMIT
        (new Regex(@"\bCOMMIT\s+TRAN(?:SACTION)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "COMMIT", "COMMIT TRANSACTION"),

        // ROLLBACK TRANSACTION -> ROLLBACK
        (new Regex(@"\bROLLBACK\s+TRAN(?:SACTION)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "ROLLBACK", "ROLLBACK TRANSACTION"),

        // GO statement (batch separator - remove)
        (new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
         "", "Remove GO"),

        // USE database (not applicable in PostgreSQL connection context)
        (new Regex(@"^\s*USE\s+\[?\w+\]?\s*;?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled),
         "", "Remove USE"),
    ];

    #endregion

    #region Unsupported Constructs

    /// <summary>
    /// Patterns that indicate constructs requiring AI or manual intervention.
    /// Each entry includes the pattern, construct type, migration suggestion, and severity.
    /// </summary>
    public static readonly List<(Regex Pattern, string ConstructType, string? Suggestion, ConstructSeverity Severity)> UnsupportedPatterns =
    [
        // Cursor-related
        (new Regex(@"\bCURSOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "CURSOR", "Consider using a set-based approach or PostgreSQL's RETURN QUERY", ConstructSeverity.Warning),

        (new Regex(@"\bDECLARE\s+\w+\s+CURSOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "DECLARE CURSOR", "PostgreSQL cursors have different syntax; use DECLARE cursor_name CURSOR FOR query", ConstructSeverity.Error),

        (new Regex(@"\bFETCH\s+NEXT\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "FETCH CURSOR", "Use PostgreSQL FETCH syntax: FETCH NEXT FROM cursor_name", ConstructSeverity.Warning),

        // Control flow
        (new Regex(@"\bGOTO\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "GOTO", "GOTO is not supported in PostgreSQL; refactor using loops or conditionals", ConstructSeverity.Error),

        (new Regex(@"\bWAITFOR\s+DELAY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "WAITFOR DELAY", "Use pg_sleep() function instead", ConstructSeverity.Warning),

        // External data access
        (new Regex(@"\bOPENROWSET\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "OPENROWSET", "Use postgres_fdw or file_fdw for external data access", ConstructSeverity.Error),

        (new Regex(@"\bOPENQUERY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "OPENQUERY", "Use postgres_fdw for linked server queries", ConstructSeverity.Error),

        // Join variants
        (new Regex(@"\bCROSS\s+APPLY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "CROSS APPLY", "Use LATERAL JOIN instead", ConstructSeverity.Warning),

        (new Regex(@"\bOUTER\s+APPLY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "OUTER APPLY", "Use LEFT JOIN LATERAL instead", ConstructSeverity.Warning),

        // Pivot operations
        (new Regex(@"\bPIVOT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "PIVOT", "Use crosstab() from tablefunc extension or CASE expressions", ConstructSeverity.Warning),

        (new Regex(@"\bUNPIVOT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "UNPIVOT", "Use UNNEST with array or VALUES clause", ConstructSeverity.Warning),

        // MERGE and OUTPUT
        (new Regex(@"\bMERGE\s+INTO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "MERGE", "Use INSERT ... ON CONFLICT (PostgreSQL 9.5+) or separate INSERT/UPDATE", ConstructSeverity.Warning),

        (new Regex(@"\bOUTPUT\s+(?:INSERTED|DELETED)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "OUTPUT clause", "Use RETURNING clause instead", ConstructSeverity.Warning),

        // XML methods
        (new Regex(@"\.(?:value|query|nodes|exist|modify)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "XML methods", "Use PostgreSQL XML functions: xpath(), xmltable()", ConstructSeverity.Warning),

        // Dynamic SQL
        (new Regex(@"\bSP_EXECUTESQL\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "sp_executesql", "Use EXECUTE or EXECUTE format() in PL/pgSQL", ConstructSeverity.Warning),

        (new Regex(@"\bEXEC\s+@", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Dynamic SQL with variables", "Review and potentially refactor dynamic SQL", ConstructSeverity.Warning),

        // Error handling
        (new Regex(@"\bBEGIN\s+TRY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "TRY...CATCH", "Use BEGIN...EXCEPTION...END in PL/pgSQL", ConstructSeverity.Warning),

        // Encryption
        (new Regex(@"\bWITH\s*\(\s*ENCRYPTION\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "WITH ENCRYPTION", "PostgreSQL doesn't support encrypted stored procedures", ConstructSeverity.Info),

        // Deprecated constructs
        (new Regex(@"\bCOMPUTE\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "COMPUTE", "COMPUTE is deprecated; use GROUP BY with ROLLUP or window functions", ConstructSeverity.Error),

        // Temp tables
        (new Regex(@"\bSELECT\s+INTO\s+#", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "SELECT INTO temp table", "Use CREATE TEMP TABLE or SELECT INTO without # prefix", ConstructSeverity.Warning),

        (new Regex(@"##\w+", RegexOptions.Compiled),
         "Global temp table", "PostgreSQL doesn't support global temp tables; use unlogged tables or regular tables", ConstructSeverity.Error),

        (new Regex(@"#\w+", RegexOptions.Compiled),
         "Temp table reference", "PostgreSQL temp tables don't use # prefix", ConstructSeverity.Warning),

        // Table variables
        (new Regex(@"\bINTO\s+@\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Table variable", "Use temp tables or CTEs instead of table variables", ConstructSeverity.Warning),

        // Full-text search
        (new Regex(@"\bCONTAINS\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Full-text CONTAINS", "Use PostgreSQL full-text search with to_tsvector/to_tsquery", ConstructSeverity.Warning),

        (new Regex(@"\bFREETEXT\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "Full-text FREETEXT", "Use PostgreSQL full-text search with to_tsvector/to_tsquery", ConstructSeverity.Warning),

        // RAISERROR (also caught by statement patterns, but flagged as unsupported for analysis)
        (new Regex(@"\bRAISERROR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
         "RAISERROR", "Use RAISE in PostgreSQL", ConstructSeverity.Warning),
    ];

    #endregion

    #region Helper Methods - Date/Time

    /// <summary>
    /// Converts MSSQL DATEADD(interval, number, date) to PostgreSQL interval arithmetic.
    /// </summary>
    private static string ConvertDateAdd(string[] args)
    {
        if (args.Length != 3) return $"DATEADD({string.Join(", ", args)})";

        var interval = args[0].ToUpperInvariant().Trim('\'', '"');
        var number = args[1];
        var date = args[2];

        if (interval is "QUARTER" or "QQ" or "Q")
        {
            return $"({date} + ({number} * 3) * INTERVAL '1 month')";
        }

        var pgInterval = GetPostgresInterval(interval);
        return $"({date} + {number} * INTERVAL '1 {pgInterval}')";
    }

    /// <summary>
    /// Converts MSSQL DATEDIFF(interval, start, end) to PostgreSQL date arithmetic.
    /// </summary>
    private static string ConvertDateDiff(string[] args)
    {
        if (args.Length != 3) return $"DATEDIFF({string.Join(", ", args)})";

        var interval = args[0].ToUpperInvariant().Trim('\'', '"');
        var startDate = args[1];
        var endDate = args[2];

        return interval switch
        {
            "YEAR" or "YY" or "YYYY" => $"(EXTRACT(YEAR FROM {endDate}) - EXTRACT(YEAR FROM {startDate}))::INTEGER",
            "MONTH" or "MM" or "M" => $"((EXTRACT(YEAR FROM {endDate}) - EXTRACT(YEAR FROM {startDate})) * 12 + EXTRACT(MONTH FROM {endDate}) - EXTRACT(MONTH FROM {startDate}))::INTEGER",
            "DAY" or "DD" or "D" => $"(DATE({endDate}) - DATE({startDate}))::INTEGER",
            "WEEK" or "WK" or "WW" => $"((DATE({endDate}) - DATE({startDate})) / 7)::INTEGER",
            "HOUR" or "HH" => $"(EXTRACT(EPOCH FROM ({endDate} - {startDate})) / 3600)::INTEGER",
            "MINUTE" or "MI" or "N" => $"(EXTRACT(EPOCH FROM ({endDate} - {startDate})) / 60)::INTEGER",
            "SECOND" or "SS" or "S" => $"EXTRACT(EPOCH FROM ({endDate} - {startDate}))::INTEGER",
            "MILLISECOND" or "MS" => $"(EXTRACT(EPOCH FROM ({endDate} - {startDate})) * 1000)::INTEGER",
            _ => $"(DATE({endDate}) - DATE({startDate}))::INTEGER"
        };
    }

    /// <summary>
    /// Converts MSSQL DATEPART(part, date) to PostgreSQL EXTRACT.
    /// </summary>
    private static string ConvertDatePart(string[] args)
    {
        if (args.Length < 2) return $"DATEPART({string.Join(", ", args)})";

        var part = args[0].ToUpperInvariant().Trim('\'', '"');
        var date = args[1];

        var pgPart = part switch
        {
            "YEAR" or "YY" or "YYYY" => "YEAR",
            "QUARTER" or "QQ" or "Q" => "QUARTER",
            "MONTH" or "MM" or "M" => "MONTH",
            "DAYOFYEAR" or "DY" or "Y" => "DOY",
            "DAY" or "DD" or "D" => "DAY",
            "WEEK" or "WK" or "WW" => "WEEK",
            "WEEKDAY" or "DW" => "DOW",
            "HOUR" or "HH" => "HOUR",
            "MINUTE" or "MI" or "N" => "MINUTE",
            "SECOND" or "SS" or "S" => "SECOND",
            "MILLISECOND" or "MS" => "MILLISECONDS",
            "MICROSECOND" or "MCS" => "MICROSECONDS",
            "ISO_WEEK" or "ISOWK" or "ISOWW" => "WEEK",
            _ => part
        };

        return $"EXTRACT({pgPart} FROM {date})::INTEGER";
    }

    /// <summary>
    /// Converts MSSQL DATENAME(part, date) to PostgreSQL TO_CHAR.
    /// </summary>
    private static string ConvertDateName(string[] args)
    {
        if (args.Length < 2) return $"DATENAME({string.Join(", ", args)})";

        var part = args[0].ToUpperInvariant().Trim('\'', '"');
        var date = args[1];

        return part switch
        {
            "MONTH" or "MM" or "M" => $"TO_CHAR({date}, 'Month')",
            "WEEKDAY" or "DW" => $"TO_CHAR({date}, 'Day')",
            _ => $"TO_CHAR({date}, '{part}')"
        };
    }

    /// <summary>
    /// Converts MSSQL EOMONTH(date [, offset]) to PostgreSQL date arithmetic.
    /// </summary>
    private static string ConvertEoMonth(string[] args)
    {
        if (args.Length == 0) return "EOMONTH()";

        var date = args[0];
        if (args.Length >= 2)
        {
            var monthOffset = args[1];
            return $"(DATE_TRUNC('month', {date}::DATE + ({monthOffset} || ' months')::INTERVAL) + INTERVAL '1 month' - INTERVAL '1 day')::DATE";
        }

        return $"(DATE_TRUNC('month', {date}::DATE) + INTERVAL '1 month' - INTERVAL '1 day')::DATE";
    }

    /// <summary>
    /// Maps MSSQL date interval abbreviations to PostgreSQL interval names.
    /// </summary>
    private static string GetPostgresInterval(string mssqlInterval)
    {
        return mssqlInterval switch
        {
            "YEAR" or "YY" or "YYYY" => "year",
            "QUARTER" or "QQ" or "Q" => "month",
            "MONTH" or "MM" or "M" => "month",
            "DAYOFYEAR" or "DY" or "Y" => "day",
            "DAY" or "DD" or "D" => "day",
            "WEEK" or "WK" or "WW" => "week",
            "HOUR" or "HH" => "hour",
            "MINUTE" or "MI" or "N" => "minute",
            "SECOND" or "SS" or "S" => "second",
            "MILLISECOND" or "MS" => "millisecond",
            "MICROSECOND" or "MCS" => "microsecond",
            "NANOSECOND" or "NS" => "microsecond", // PostgreSQL doesn't support nanoseconds
            _ => mssqlInterval.ToLowerInvariant()
        };
    }

    #endregion

    #region Helper Methods - String/Format

    /// <summary>
    /// Converts MSSQL STR(value [, length [, decimals]]) to PostgreSQL LPAD/ROUND.
    /// </summary>
    private static string ConvertStr(string[] args)
    {
        if (args.Length == 0) return "STR()";

        var value = args[0];
        var length = args.Length >= 2 ? args[1] : "10";
        var decimals = args.Length >= 3 ? args[2] : "0";

        return $"LPAD(ROUND({value}::NUMERIC, {decimals})::TEXT, {length})";
    }

    /// <summary>
    /// Converts MSSQL FORMAT(value, format) to PostgreSQL TO_CHAR.
    /// </summary>
    private static string ConvertFormat(string[] args)
    {
        if (args.Length < 2) return $"FORMAT({string.Join(", ", args)})";

        var value = args[0];
        var format = args[1].Trim('\'');

        // Common format patterns
        return format.ToUpperInvariant() switch
        {
            "D" => $"TO_CHAR({value}::DATE, 'YYYY-MM-DD')",
            "T" => $"TO_CHAR({value}, 'HH24:MI:SS')",
            "G" => $"{value}::TEXT",
            "N0" => $"TO_CHAR({value}, 'FM999,999,999,999')",
            "N2" => $"TO_CHAR({value}, 'FM999,999,999,999.00')",
            "C" => $"TO_CHAR({value}, 'FM$999,999,999,999.00')",
            "P" => $"TO_CHAR({value} * 100, 'FM999.00%')",
            _ => $"TO_CHAR({value}, '{format}')"
        };
    }

    /// <summary>
    /// Converts MSSQL PATINDEX(pattern, value) to PostgreSQL regex position.
    /// </summary>
    private static string ConvertPatIndex(string[] args)
    {
        if (args.Length < 2) return $"PATINDEX({string.Join(", ", args)})";

        var pattern = args[0].Trim('\'');
        var value = args[1];

        // Convert SQL pattern to regex
        pattern = pattern.Replace("%", ".*").Replace("_", ".");

        return $"(CASE WHEN {value} ~ '{pattern}' THEN POSITION(SUBSTRING({value} FROM '{pattern}') IN {value}) ELSE 0 END)";
    }

    #endregion

    #region Helper Methods - Type Conversion

    /// <summary>
    /// Converts MSSQL CAST(value AS type) to PostgreSQL CAST.
    /// </summary>
    private static string ConvertCast(string[] args)
    {
        if (args.Length < 2) return $"CAST({string.Join(", ", args)})";

        var value = args[0];
        var type = ConvertDataTypeInline(args[1]);

        return $"CAST({value} AS {type})";
    }

    /// <summary>
    /// Converts MSSQL CONVERT(type, value [, style]) to PostgreSQL CAST or TO_CHAR.
    /// </summary>
    private static string ConvertConvert(string[] args)
    {
        if (args.Length < 2) return $"CONVERT({string.Join(", ", args)})";

        var type = ConvertDataTypeInline(args[0]);
        var value = args[1];

        // Handle style parameter for date/string conversions
        if (args.Length >= 3)
        {
            var style = args[2];
            // Date format styles
            if (type.Contains("TIMESTAMP", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("DATE", StringComparison.OrdinalIgnoreCase))
            {
                return ConvertDateStyle(value, type, style);
            }
        }

        return $"CAST({value} AS {type})";
    }

    /// <summary>
    /// Converts MSSQL TRY_CAST(value AS type) to PostgreSQL safe cast.
    /// </summary>
    private static string ConvertTryCast(string[] args)
    {
        if (args.Length < 2) return $"TRY_CAST({string.Join(", ", args)})";

        var value = args[0];
        var type = ConvertDataTypeInline(args[1]);

        // PostgreSQL doesn't have TRY_CAST; use a CASE with null check
        return $"(CASE WHEN {value} IS NOT NULL THEN {value}::{type} ELSE NULL END)";
    }

    /// <summary>
    /// Converts MSSQL TRY_CONVERT(type, value) to PostgreSQL safe cast.
    /// </summary>
    private static string ConvertTryConvert(string[] args)
    {
        if (args.Length < 2) return $"TRY_CONVERT({string.Join(", ", args)})";

        var type = ConvertDataTypeInline(args[0]);
        var value = args[1];

        return $"(CASE WHEN {value} IS NOT NULL THEN {value}::{type} ELSE NULL END)";
    }

    /// <summary>
    /// Converts MSSQL PARSE(value AS type) to PostgreSQL CAST.
    /// </summary>
    private static string ConvertParse(string[] args)
    {
        if (args.Length < 2) return $"PARSE({string.Join(", ", args)})";

        var value = args[0];
        var type = ConvertDataTypeInline(args[1].Replace(" USING ", " ")); // Remove USING culture

        return $"CAST({value} AS {type})";
    }

    /// <summary>
    /// Converts MSSQL TRY_PARSE(value AS type) to PostgreSQL safe cast.
    /// </summary>
    private static string ConvertTryParse(string[] args)
    {
        if (args.Length < 2) return $"TRY_PARSE({string.Join(", ", args)})";

        var value = args[0];
        var type = ConvertDataTypeInline(args[1].Replace(" USING ", " "));

        return $"(CASE WHEN {value} IS NOT NULL THEN {value}::{type} ELSE NULL END)";
    }

    /// <summary>
    /// Converts MSSQL CONVERT date style codes to PostgreSQL TO_CHAR format strings.
    /// Supports all standard MSSQL date style codes (101-127).
    /// </summary>
    private static string ConvertDateStyle(string value, string type, string style)
    {
        var format = style switch
        {
            "101" => "MM/DD/YYYY",
            "102" => "YYYY.MM.DD",
            "103" => "DD/MM/YYYY",
            "104" => "DD.MM.YYYY",
            "105" => "DD-MM-YYYY",
            "106" => "DD Mon YYYY",
            "107" => "Mon DD, YYYY",
            "108" => "HH24:MI:SS",
            "109" => "Mon DD YYYY HH12:MI:SS:MSAM",
            "110" => "MM-DD-YYYY",
            "111" => "YYYY/MM/DD",
            "112" => "YYYYMMDD",
            "113" => "DD Mon YYYY HH24:MI:SS:MS",
            "114" => "HH24:MI:SS:MS",
            "120" => "YYYY-MM-DD HH24:MI:SS",
            "121" => "YYYY-MM-DD HH24:MI:SS.MS",
            "126" => "YYYY-MM-DD\"T\"HH24:MI:SS.MS",
            "127" => "YYYY-MM-DD\"T\"HH24:MI:SS.MSTZ",
            _ => "YYYY-MM-DD HH24:MI:SS"
        };

        if (type.Contains("CHAR", StringComparison.OrdinalIgnoreCase) || type.Contains("TEXT", StringComparison.OrdinalIgnoreCase))
        {
            return $"TO_CHAR({value}, '{format}')";
        }

        return $"TO_TIMESTAMP({value}, '{format}')";
    }

    /// <summary>
    /// Converts an inline MSSQL data type reference to its PostgreSQL equivalent.
    /// Handles types with parameters like VARCHAR(50).
    /// </summary>
    public static string ConvertDataTypeInline(string mssqlType)
    {
        var type = mssqlType.Trim().ToUpperInvariant();

        // Handle types with parameters like VARCHAR(50)
        var match = Regex.Match(type, @"^(\w+)\s*(?:\(([^)]+)\))?$");
        if (match.Success)
        {
            var baseType = match.Groups[1].Value;
            var parameters = match.Groups[2].Success ? match.Groups[2].Value : null;

            if (DataTypeMappings.TryGetValue(baseType, out var pgType))
            {
                if (parameters != null && !pgType.Contains('('))
                {
                    return $"{pgType}({parameters})";
                }
                return pgType;
            }
        }

        return type;
    }

    #endregion

    #region Helper Methods - JSON

    /// <summary>
    /// Converts MSSQL JSON path like '$.name' to PostgreSQL array like '{name}'.
    /// </summary>
    private static string ConvertJsonPath(string mssqlPath)
    {
        var path = mssqlPath.Trim('\'', '"');
        if (path.StartsWith("$."))
        {
            path = path[2..];
        }
        else if (path.StartsWith("$"))
        {
            path = path[1..];
        }

        var parts = path.Split('.');
        return "'{" + string.Join(",", parts) + "}'";
    }

    /// <summary>
    /// Converts MSSQL JSON_MODIFY(json, path, value) to PostgreSQL JSONB_SET.
    /// </summary>
    private static string ConvertJsonModify(string[] args)
    {
        if (args.Length < 3) return $"JSON_MODIFY({string.Join(", ", args)})";

        var json = args[0];
        var path = ConvertJsonPath(args[1]);
        var value = args[2];

        return $"JSONB_SET({json}::JSONB, {path}, {value}::JSONB)";
    }

    #endregion
}
