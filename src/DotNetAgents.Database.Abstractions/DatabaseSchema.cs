namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a complete database schema with all its objects and metadata.
/// This is the root entity that contains all database objects for analysis and operations.
/// </summary>
public sealed class DatabaseSchema
{
    /// <summary>
    /// Gets the name of the database.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the list of tables in this database.
    /// </summary>
    public List<Table> Tables { get; init; } = [];

    /// <summary>
    /// Gets the list of views in this database.
    /// </summary>
    public List<View> Views { get; init; } = [];

    /// <summary>
    /// Gets the list of stored procedures in this database.
    /// </summary>
    public List<StoredProcedure> StoredProcedures { get; init; } = [];

    /// <summary>
    /// Gets the list of functions in this database.
    /// </summary>
    public List<Function> Functions { get; init; } = [];

    /// <summary>
    /// Gets the list of sequences in this database.
    /// </summary>
    public List<Sequence> Sequences { get; init; } = [];

    /// <summary>
    /// Gets the database version information.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the database collation.
    /// </summary>
    public string? Collation { get; init; }

    /// <summary>
    /// Gets the database character set.
    /// </summary>
    public string? CharacterSet { get; init; }

    /// <summary>
    /// Gets the database size in bytes.
    /// Null if size is unknown.
    /// </summary>
    public long? SizeInBytes { get; init; }

    /// <summary>
    /// Gets the database creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the date when the schema was analyzed.
    /// </summary>
    public DateTime AnalyzedDate { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets additional metadata about the database analysis.
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseSchema"/> class.
    /// </summary>
    public DatabaseSchema()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseSchema"/> class.
    /// </summary>
    /// <param name="name">The database name.</param>
    public DatabaseSchema(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the total number of database objects.
    /// </summary>
    public int TotalObjectCount => Tables.Count + Views.Count + StoredProcedures.Count + Functions.Count + Sequences.Count;

    /// <summary>
    /// Gets the total number of tables.
    /// </summary>
    public int TableCount => Tables.Count;

    /// <summary>
    /// Gets the total number of columns across all tables.
    /// </summary>
    public int TotalColumnCount => Tables.Sum(t => t.ColumnCount);

    /// <summary>
    /// Gets the total estimated row count across all tables.
    /// </summary>
    public long TotalEstimatedRows => Tables.Where(t => t.EstimatedRowCount.HasValue).Sum(t => t.EstimatedRowCount!.Value);

    /// <summary>
    /// Gets all unique schema names in the database.
    /// </summary>
    public IEnumerable<string> SchemaNames
    {
        get
        {
            var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in Tables)
                schemas.Add(table.SchemaName);

            foreach (var view in Views)
                schemas.Add(view.SchemaName);

            foreach (var proc in StoredProcedures)
                schemas.Add(proc.SchemaName);

            foreach (var func in Functions)
                schemas.Add(func.SchemaName);

            foreach (var seq in Sequences)
                schemas.Add(seq.SchemaName);

            return schemas.OrderBy(s => s);
        }
    }

    /// <summary>
    /// Gets tables that have identity columns.
    /// </summary>
    public IEnumerable<Table> TablesWithIdentity => Tables.Where(t => t.HasIdentityColumns);

    /// <summary>
    /// Gets tables that have foreign key relationships.
    /// </summary>
    public IEnumerable<Table> TablesWithForeignKeys => Tables.Where(t => t.HasForeignKeys);

    /// <summary>
    /// Gets all foreign key relationships in the database.
    /// </summary>
    public IEnumerable<ForeignKey> AllForeignKeys => Tables.SelectMany(t => t.ForeignKeys);

    /// <summary>
    /// Finds a table by schema and name (case-insensitive).
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The table if found; otherwise, null.</returns>
    public Table? FindTable(string schemaName, string tableName)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(tableName))
            return null;

        return Tables.FirstOrDefault(t =>
            string.Equals(t.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.TableName, tableName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds a view by schema and name (case-insensitive).
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="viewName">The view name.</param>
    /// <returns>The view if found; otherwise, null.</returns>
    public View? FindView(string schemaName, string viewName)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(viewName))
            return null;

        return Views.FirstOrDefault(v =>
            string.Equals(v.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(v.ViewName, viewName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets tables in a specific schema.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>Tables in the specified schema.</returns>
    public IEnumerable<Table> GetTablesInSchema(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
            return [];

        return Tables.Where(t => string.Equals(t.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets objects in a specific schema.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <returns>A summary of objects in the specified schema.</returns>
    public SchemaObjectSummary GetSchemaObjectSummary(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName))
            return new SchemaObjectSummary(schemaName ?? "");

        return new SchemaObjectSummary(schemaName)
        {
            TableCount = Tables.Count(t => string.Equals(t.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)),
            ViewCount = Views.Count(v => string.Equals(v.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)),
            StoredProcedureCount = StoredProcedures.Count(p => string.Equals(p.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)),
            FunctionCount = Functions.Count(f => string.Equals(f.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase)),
            SequenceCount = Sequences.Count(s => string.Equals(s.SchemaName, schemaName, StringComparison.OrdinalIgnoreCase))
        };
    }

    /// <summary>
    /// Validates the entire database schema for consistency.
    /// </summary>
    /// <returns>A list of validation errors, or empty list if valid.</returns>
    public List<string> ValidateSchema()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Database name cannot be empty");

        // Validate all tables
        foreach (var table in Tables)
        {
            var tableErrors = table.ValidateStructure();
            errors.AddRange(tableErrors.Select(e => $"Table {table.FullyQualifiedName}: {e}"));
        }

        // Check for duplicate table names within the same schema
        var duplicateTables = Tables
            .GroupBy(t => $"{t.SchemaName.ToUpperInvariant()}.{t.TableName.ToUpperInvariant()}")
            .Where(g => g.Count() > 1)
            .Select(g => g.First().FullyQualifiedName);

        foreach (var duplicate in duplicateTables)
        {
            errors.Add($"Duplicate table name: {duplicate}");
        }

        // Validate foreign key references
        foreach (var fk in AllForeignKeys)
        {
            var referencedTable = FindTable(fk.ReferencedSchemaName, fk.ReferencedTableName);
            if (referencedTable is null)
            {
                errors.Add($"Foreign key '{fk.Name}' references non-existent table: {fk.ReferencedSchemaName}.{fk.ReferencedTableName}");
                continue;
            }

            // Check if referenced columns exist
            foreach (var refColumn in fk.ReferencedColumnNames)
            {
                if (referencedTable.FindColumn(refColumn) is null)
                {
                    errors.Add($"Foreign key '{fk.Name}' references non-existent column: {fk.ReferencedSchemaName}.{fk.ReferencedTableName}.{refColumn}");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Gets statistics about the database schema.
    /// </summary>
    /// <returns>A summary of database statistics.</returns>
    public DatabaseStatistics GetStatistics()
    {
        return new DatabaseStatistics
        {
            DatabaseName = Name,
            TotalObjectCount = TotalObjectCount,
            TableCount = TableCount,
            ViewCount = Views.Count,
            StoredProcedureCount = StoredProcedures.Count,
            FunctionCount = Functions.Count,
            SequenceCount = Sequences.Count,
            TotalColumnCount = TotalColumnCount,
            TotalEstimatedRows = TotalEstimatedRows,
            SchemaCount = SchemaNames.Count(),
            TablesWithIdentityCount = TablesWithIdentity.Count(),
            TablesWithForeignKeysCount = TablesWithForeignKeys.Count(),
            TotalForeignKeyCount = AllForeignKeys.Count(),
            AnalyzedDate = AnalyzedDate
        };
    }

    /// <summary>
    /// Returns a string representation of the database schema.
    /// </summary>
    /// <returns>A string that represents the current database schema.</returns>
    public override string ToString() => $"Database: {Name} ({TotalObjectCount} objects, {TableCount} tables)";

    /// <summary>
    /// Determines whether the specified object is equal to the current database schema.
    /// </summary>
    /// <param name="obj">The object to compare with the current database schema.</param>
    /// <returns>True if the specified object is equal to the current database schema; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is DatabaseSchema other &&
               Name == other.Name &&
               Tables.SequenceEqual(other.Tables) &&
               Views.SequenceEqual(other.Views) &&
               StoredProcedures.SequenceEqual(other.StoredProcedures) &&
               Functions.SequenceEqual(other.Functions) &&
               Sequences.SequenceEqual(other.Sequences);
    }

    /// <summary>
    /// Returns the hash code for this database schema.
    /// </summary>
    /// <returns>A hash code for the current database schema.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        hash.Add(Tables.Count);
        hash.Add(Views.Count);
        hash.Add(StoredProcedures.Count);
        hash.Add(Functions.Count);
        hash.Add(Sequences.Count);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a summary of database objects in a specific schema.
/// </summary>
public sealed record SchemaObjectSummary(string SchemaName)
{
    /// <summary>
    /// Gets the number of tables in the schema.
    /// </summary>
    public int TableCount { get; init; }

    /// <summary>
    /// Gets the number of views in the schema.
    /// </summary>
    public int ViewCount { get; init; }

    /// <summary>
    /// Gets the number of stored procedures in the schema.
    /// </summary>
    public int StoredProcedureCount { get; init; }

    /// <summary>
    /// Gets the number of functions in the schema.
    /// </summary>
    public int FunctionCount { get; init; }

    /// <summary>
    /// Gets the number of sequences in the schema.
    /// </summary>
    public int SequenceCount { get; init; }

    /// <summary>
    /// Gets the total number of objects in the schema.
    /// </summary>
    public int TotalObjectCount => TableCount + ViewCount + StoredProcedureCount + FunctionCount + SequenceCount;
}

/// <summary>
/// Represents database statistics for reporting and analysis.
/// </summary>
public sealed record DatabaseStatistics
{
    /// <summary>
    /// Gets the database name.
    /// </summary>
    public required string DatabaseName { get; init; }

    /// <summary>
    /// Gets the total number of database objects.
    /// </summary>
    public int TotalObjectCount { get; init; }

    /// <summary>
    /// Gets the number of tables.
    /// </summary>
    public int TableCount { get; init; }

    /// <summary>
    /// Gets the number of views.
    /// </summary>
    public int ViewCount { get; init; }

    /// <summary>
    /// Gets the number of stored procedures.
    /// </summary>
    public int StoredProcedureCount { get; init; }

    /// <summary>
    /// Gets the number of functions.
    /// </summary>
    public int FunctionCount { get; init; }

    /// <summary>
    /// Gets the number of sequences.
    /// </summary>
    public int SequenceCount { get; init; }

    /// <summary>
    /// Gets the total number of columns across all tables.
    /// </summary>
    public int TotalColumnCount { get; init; }

    /// <summary>
    /// Gets the total estimated number of rows across all tables.
    /// </summary>
    public long TotalEstimatedRows { get; init; }

    /// <summary>
    /// Gets the number of schemas.
    /// </summary>
    public int SchemaCount { get; init; }

    /// <summary>
    /// Gets the number of tables with identity columns.
    /// </summary>
    public int TablesWithIdentityCount { get; init; }

    /// <summary>
    /// Gets the number of tables with foreign keys.
    /// </summary>
    public int TablesWithForeignKeysCount { get; init; }

    /// <summary>
    /// Gets the total number of foreign key constraints.
    /// </summary>
    public int TotalForeignKeyCount { get; init; }

    /// <summary>
    /// Gets the date when the schema was analyzed.
    /// </summary>
    public DateTime AnalyzedDate { get; init; }
}
