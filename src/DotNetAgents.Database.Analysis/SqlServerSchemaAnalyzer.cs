using Dapper;
using DotNetAgents.Database.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// SQL Server schema analyzer that extracts database schema information.
/// </summary>
public sealed class SqlServerSchemaAnalyzer : ISchemaAnalyzer
{
    private readonly ILogger<SqlServerSchemaAnalyzer>? _logger;
    private const int DefaultCommandTimeout = 300;

    /// <summary>
    /// Gets the database provider type this analyzer supports.
    /// </summary>
    public string ProviderType => "SqlServer";

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerSchemaAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public SqlServerSchemaAnalyzer(ILogger<SqlServerSchemaAnalyzer>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DatabaseSchema> AnalyzeAsync(
        string connectionString,
        SchemaAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        options ??= new SchemaAnalysisOptions();

        _logger?.LogInformation("Starting SQL Server database schema analysis");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Get database name
        var databaseName = await connection.QuerySingleAsync<string>(
            "SELECT DB_NAME()",
            commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        _logger?.LogInformation("Analyzing database: {DatabaseName}", databaseName);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Analyze objects in parallel
        var tablesTask = AnalyzeTablesAsync(connection, options, cancellationToken);
        var viewsTask = AnalyzeViewsAsync(connection, options, cancellationToken);
        var proceduresTask = AnalyzeStoredProceduresAsync(connection, options, cancellationToken);
        var functionsTask = AnalyzeFunctionsAsync(connection, options, cancellationToken);
        var sequencesTask = AnalyzeSequencesAsync(connection, options, cancellationToken);

        await Task.WhenAll(tablesTask, viewsTask, proceduresTask, functionsTask, sequencesTask).ConfigureAwait(false);

        var tables = await tablesTask.ConfigureAwait(false);
        var views = await viewsTask.ConfigureAwait(false);
        var storedProcedures = await proceduresTask.ConfigureAwait(false);
        var functions = await functionsTask.ConfigureAwait(false);
        var sequences = await sequencesTask.ConfigureAwait(false);

        stopwatch.Stop();

        _logger?.LogInformation(
            "Analysis completed in {ElapsedMs}ms: {TableCount} tables, {ViewCount} views, {ProcCount} procedures, {FunctionCount} functions",
            stopwatch.ElapsedMilliseconds,
            tables.Count,
            views.Count,
            storedProcedures.Count,
            functions.Count);

        return new DatabaseSchema
        {
            Name = databaseName,
            Tables = tables,
            Views = views,
            StoredProcedures = storedProcedures,
            Functions = functions,
            Sequences = sequences,
            AnalyzedDate = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<bool> ValidateConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Connection validation failed");
            return false;
        }
    }

    private async Task<List<Table>> AnalyzeTablesAsync(
        DbConnection connection,
        SchemaAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var tables = new List<Table>();

        var tableQuery = @"
            SELECT
                s.name AS SchemaName,
                t.name AS TableName,
                t.object_id AS ObjectId
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            " + (options.IncludeSystemObjects ? "" : "WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')") + @"
            ORDER BY s.name, t.name";

        var tableInfos = await connection.QueryAsync(tableQuery, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        foreach (var tableInfo in tableInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columns = await AnalyzeTableColumnsAsync(connection, tableInfo.ObjectId, cancellationToken).ConfigureAwait(false);
            var constraints = await AnalyzeTableConstraintsAsync(connection, tableInfo.ObjectId, cancellationToken).ConfigureAwait(false);

            tables.Add(new Table
            {
                SchemaName = tableInfo.SchemaName,
                TableName = tableInfo.TableName,
                Columns = columns,
                PrimaryKey = constraints.PrimaryKey,
                ForeignKeys = constraints.ForeignKeys,
                Indexes = constraints.Indexes,
                CheckConstraints = constraints.CheckConstraints,
                DefaultConstraints = constraints.DefaultConstraints
            });
        }

        return tables;
    }

    private async Task<List<Column>> AnalyzeTableColumnsAsync(
        DbConnection connection,
        int objectId,
        CancellationToken cancellationToken)
    {
        var columnQuery = @"
            SELECT
                c.name AS ColumnName,
                t.name AS DataType,
                c.max_length AS MaxLength,
                c.precision AS Precision,
                c.scale AS Scale,
                c.is_nullable AS IsNullable,
                c.is_identity AS IsIdentity,
                ISNULL(ic.seed_value, 0) AS IdentitySeed,
                ISNULL(ic.increment_value, 0) AS IdentityIncrement,
                dc.definition AS DefaultValue
            FROM sys.columns c
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            LEFT JOIN sys.identity_columns ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
            WHERE c.object_id = @ObjectId
            ORDER BY c.column_id";

        var columns = await connection.QueryAsync(columnQuery, new { ObjectId = objectId }, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return columns.Select(col => new Column
        {
            Name = col.ColumnName,
            DataType = col.DataType,
            MaxLength = col.MaxLength == -1 ? null : (int?)col.MaxLength,
            Precision = col.Precision == 0 ? null : (int?)col.Precision,
            Scale = col.Scale == 0 ? null : (int?)col.Scale,
            IsNullable = col.IsNullable,
            IsIdentity = col.IsIdentity,
            IdentitySeed = col.IsIdentity ? (long?)col.IdentitySeed : null,
            IdentityIncrement = col.IsIdentity ? (long?)col.IdentityIncrement : null,
            DefaultValue = col.DefaultValue
        }).ToList();
    }

    private async Task<(PrimaryKey?, List<ForeignKey>, List<Abstractions.Index>, List<CheckConstraint>, List<DefaultConstraint>)> AnalyzeTableConstraintsAsync(
        DbConnection connection,
        int objectId,
        CancellationToken cancellationToken)
    {
        // Get primary key
        var pkQuery = @"
            SELECT
                kc.name AS KeyName,
                STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS ColumnNames
            FROM sys.key_constraints kc
            INNER JOIN sys.index_columns ic ON kc.parent_object_id = ic.object_id AND kc.unique_index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE kc.type = 'PK' AND kc.parent_object_id = @ObjectId
            GROUP BY kc.name";

        var pkResult = await connection.QuerySingleOrDefaultAsync(pkQuery, new { ObjectId = objectId }, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        PrimaryKey? primaryKey = null;
        if (pkResult != null)
        {
            primaryKey = new PrimaryKey
            {
                Name = pkResult.KeyName,
                ColumnNames = pkResult.ColumnNames.Split(',').ToList()
            };
        }

        // Simplified - return empty collections for other constraints
        // Full implementation would populate these
        return (primaryKey, new List<ForeignKey>(), new List<Abstractions.Index>(), new List<CheckConstraint>(), new List<DefaultConstraint>());
    }

    private async Task<List<View>> AnalyzeViewsAsync(
        DbConnection connection,
        SchemaAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var query = @"
            SELECT
                s.name AS SchemaName,
                v.name AS ViewName,
                m.definition AS Definition
            FROM sys.views v
            INNER JOIN sys.schemas s ON v.schema_id = s.schema_id
            INNER JOIN sys.sql_modules m ON v.object_id = m.object_id
            " + (options.IncludeSystemObjects ? "" : "WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')") + @"
            ORDER BY s.name, v.name";

        var views = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return views.Select(v => new View
        {
            SchemaName = v.SchemaName,
            ViewName = v.ViewName,
            Definition = v.Definition,
            Columns = new List<Column>()
        }).ToList();
    }

    private async Task<List<StoredProcedure>> AnalyzeStoredProceduresAsync(
        DbConnection connection,
        SchemaAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var query = @"
            SELECT
                s.name AS SchemaName,
                p.name AS ProcedureName,
                m.definition AS Definition
            FROM sys.procedures p
            INNER JOIN sys.schemas s ON p.schema_id = s.schema_id
            INNER JOIN sys.sql_modules m ON p.object_id = m.object_id
            " + (options.IncludeSystemObjects ? "" : "WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')") + @"
            ORDER BY s.name, p.name";

        var procedures = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return procedures.Select(p => new StoredProcedure
        {
            SchemaName = p.SchemaName,
            ProcedureName = p.ProcedureName,
            Definition = p.Definition,
            Parameters = new List<Parameter>()
        }).ToList();
    }

    private async Task<List<Function>> AnalyzeFunctionsAsync(
        DbConnection connection,
        SchemaAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var query = @"
            SELECT
                s.name AS SchemaName,
                o.name AS FunctionName,
                m.definition AS Definition,
                o.type_desc AS FunctionType
            FROM sys.objects o
            INNER JOIN sys.schemas s ON o.schema_id = s.schema_id
            INNER JOIN sys.sql_modules m ON o.object_id = m.object_id
            WHERE o.type IN ('FN', 'IF', 'TF')
            " + (options.IncludeSystemObjects ? "" : "AND s.name NOT IN ('sys', 'INFORMATION_SCHEMA')") + @"
            ORDER BY s.name, o.name";

        var functions = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return functions.Select(f =>
        {
            var functionType = f.FunctionType switch
            {
                "SQL_SCALAR_FUNCTION" => FunctionType.Scalar,
                "SQL_TABLE_VALUED_FUNCTION" => FunctionType.TableValued,
                "SQL_INLINE_TABLE_VALUED_FUNCTION" => FunctionType.TableValued,
                _ => FunctionType.Scalar
            };

            return new Function
            {
                SchemaName = f.SchemaName,
                FunctionName = f.FunctionName,
                Definition = f.Definition,
                ReturnType = "Unknown",
                FunctionType = functionType,
                Parameters = new List<Parameter>()
            };
        }).ToList();
    }

    private async Task<List<Sequence>> AnalyzeSequencesAsync(
        DbConnection connection,
        SchemaAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        var query = @"
            SELECT
                s.name AS SchemaName,
                seq.name AS SequenceName,
                seq.start_value AS StartValue,
                seq.increment AS IncrementValue,
                seq.minimum_value AS MinValue,
                seq.maximum_value AS MaxValue,
                t.name AS DataType
            FROM sys.sequences seq
            INNER JOIN sys.schemas s ON seq.schema_id = s.schema_id
            INNER JOIN sys.types t ON seq.user_type_id = t.user_type_id
            " + (options.IncludeSystemObjects ? "" : "WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')") + @"
            ORDER BY s.name, seq.name";

        var sequences = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return sequences.Select(seq => new Sequence
        {
            SchemaName = seq.SchemaName,
            SequenceName = seq.SequenceName,
            DataType = seq.DataType,
            StartValue = seq.StartValue,
            IncrementBy = seq.IncrementValue,
            MinValue = seq.MinValue,
            MaxValue = seq.MaxValue
        }).ToList();
    }
}
