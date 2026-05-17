using Dapper;
using DotNetAgents.Database.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data.Common;

namespace DotNetAgents.Database.Analysis;

/// <summary>
/// PostgreSQL schema analyzer that extracts database schema information.
/// </summary>
public sealed class PostgreSQLSchemaAnalyzer : ISchemaAnalyzer
{
    private readonly ILogger<PostgreSQLSchemaAnalyzer>? _logger;
    private const int DefaultCommandTimeout = 300;

    /// <summary>
    /// Gets the database provider type this analyzer supports.
    /// </summary>
    public string ProviderType => "PostgreSQL";

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLSchemaAnalyzer"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public PostgreSQLSchemaAnalyzer(ILogger<PostgreSQLSchemaAnalyzer>? logger = null)
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

        _logger?.LogInformation("Starting PostgreSQL database schema analysis");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Get database name
        var databaseName = await connection.QuerySingleAsync<string>(
            "SELECT current_database()",
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
            await using var connection = new NpgsqlConnection(connectionString);
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
                table_schema AS SchemaName,
                table_name AS TableName
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
            " + (options.IncludeSystemObjects ? "" : "AND table_schema NOT IN ('pg_catalog', 'information_schema', 'pg_toast')") + @"
            ORDER BY table_schema, table_name";

        var tableInfos = await connection.QueryAsync(tableQuery, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        foreach (var tableInfo in tableInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columns = await AnalyzeTableColumnsAsync(connection, tableInfo.SchemaName, tableInfo.TableName, cancellationToken).ConfigureAwait(false);
            var constraints = await AnalyzeTableConstraintsAsync(connection, tableInfo.SchemaName, tableInfo.TableName, cancellationToken).ConfigureAwait(false);

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
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columnQuery = @"
            SELECT
                column_name AS ColumnName,
                data_type AS DataType,
                character_maximum_length AS MaxLength,
                numeric_precision AS Precision,
                numeric_scale AS Scale,
                is_nullable = 'YES' AS IsNullable,
                column_default AS DefaultValue
            FROM information_schema.columns
            WHERE table_schema = @SchemaName AND table_name = @TableName
            ORDER BY ordinal_position";

        var columns = await connection.QueryAsync(
            columnQuery,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return columns.Select(col => new Column
        {
            Name = col.ColumnName,
            DataType = col.DataType,
            MaxLength = col.MaxLength,
            Precision = col.Precision,
            Scale = col.Scale,
            IsNullable = col.IsNullable,
            IsIdentity = false, // PostgreSQL uses sequences, not identity columns
            DefaultValue = col.DefaultValue
        }).ToList();
    }

    private async Task<(PrimaryKey?, List<ForeignKey>, List<Abstractions.Index>, List<CheckConstraint>, List<DefaultConstraint>)> AnalyzeTableConstraintsAsync(
        DbConnection connection,
        string schemaName,
        string tableName,
        CancellationToken cancellationToken)
    {
        // Get primary key
        var pkQuery = @"
            SELECT
                tc.constraint_name AS KeyName,
                string_agg(kcu.column_name, ',' ORDER BY kcu.ordinal_position) AS ColumnNames
            FROM information_schema.table_constraints tc
            INNER JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
                AND tc.table_schema = kcu.table_schema
            WHERE tc.constraint_type = 'PRIMARY KEY'
                AND tc.table_schema = @SchemaName
                AND tc.table_name = @TableName
            GROUP BY tc.constraint_name";

        var pkResult = await connection.QuerySingleOrDefaultAsync(
            pkQuery,
            new { SchemaName = schemaName, TableName = tableName },
            commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

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
                table_schema AS SchemaName,
                table_name AS ViewName,
                view_definition AS Definition
            FROM information_schema.views
            " + (options.IncludeSystemObjects ? "" : "WHERE table_schema NOT IN ('pg_catalog', 'information_schema')") + @"
            ORDER BY table_schema, table_name";

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
        // PostgreSQL doesn't have stored procedures in the same way as SQL Server
        // Functions can be used as procedures
        var query = @"
            SELECT
                n.nspname AS SchemaName,
                p.proname AS ProcedureName,
                pg_get_functiondef(p.oid) AS Definition
            FROM pg_proc p
            INNER JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE p.prokind = 'p'
            " + (options.IncludeSystemObjects ? "" : "AND n.nspname NOT IN ('pg_catalog', 'information_schema')") + @"
            ORDER BY n.nspname, p.proname";

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
                n.nspname AS SchemaName,
                p.proname AS FunctionName,
                pg_get_functiondef(p.oid) AS Definition,
                pg_get_function_result(p.oid) AS ReturnType,
                p.prokind AS FunctionKind
            FROM pg_proc p
            INNER JOIN pg_namespace n ON p.pronamespace = n.oid
            WHERE p.prokind IN ('f', 'a', 'w')
            " + (options.IncludeSystemObjects ? "" : "AND n.nspname NOT IN ('pg_catalog', 'information_schema')") + @"
            ORDER BY n.nspname, p.proname";

        var functions = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return functions.Select(f =>
        {
            var functionType = f.FunctionKind switch
            {
                "f" => FunctionType.Scalar,
                "a" => FunctionType.Aggregate,
                "w" => FunctionType.Window,
                _ => FunctionType.Scalar
            };

            return new Function
            {
                SchemaName = f.SchemaName,
                FunctionName = f.FunctionName,
                Definition = f.Definition,
                ReturnType = f.ReturnType ?? "unknown",
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
                n.nspname AS SchemaName,
                c.relname AS SequenceName,
                s.seqstart AS StartValue,
                s.seqincrement AS IncrementValue,
                s.seqmin AS MinValue,
                s.seqmax AS MaxValue,
                s.seqcache AS CacheSize,
                s.seqcycle AS IsCycling
            FROM pg_sequence s
            INNER JOIN pg_class c ON s.seqrelid = c.oid
            INNER JOIN pg_namespace n ON c.relnamespace = n.oid
            " + (options.IncludeSystemObjects ? "" : "WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')") + @"
            ORDER BY n.nspname, c.relname";

        var sequences = await connection.QueryAsync(query, commandTimeout: DefaultCommandTimeout).ConfigureAwait(false);

        return sequences.Select(seq => new Sequence
        {
            SchemaName = seq.SchemaName,
            SequenceName = seq.SequenceName,
            DataType = "bigint", // PostgreSQL sequences are typically bigint
            StartValue = seq.StartValue,
            IncrementBy = seq.IncrementValue,
            MinValue = seq.MinValue,
            MaxValue = seq.MaxValue,
            CacheSize = seq.CacheSize,
            IsCycling = seq.IsCycling
        }).ToList();
    }
}
