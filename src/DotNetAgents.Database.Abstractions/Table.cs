namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database table with its metadata, columns, constraints, and indexes.
/// This entity captures all necessary information for database schema analysis and operations.
/// </summary>
public sealed class Table
{
    /// <summary>
    /// Gets the schema name that contains this table.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets the name of the table.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Gets the list of columns in this table.
    /// </summary>
    public required List<Column> Columns { get; init; } = [];

    /// <summary>
    /// Gets the primary key constraint for this table.
    /// Null if the table has no primary key.
    /// </summary>
    public PrimaryKey? PrimaryKey { get; init; }

    /// <summary>
    /// Gets the list of foreign key constraints for this table.
    /// </summary>
    public List<ForeignKey> ForeignKeys { get; init; } = [];

    /// <summary>
    /// Gets the list of indexes on this table.
    /// </summary>
    public List<Index> Indexes { get; init; } = [];

    /// <summary>
    /// Gets the list of check constraints on this table.
    /// </summary>
    public List<CheckConstraint> CheckConstraints { get; init; } = [];

    /// <summary>
    /// Gets the list of default constraints on this table.
    /// </summary>
    public List<DefaultConstraint> DefaultConstraints { get; init; } = [];

    /// <summary>
    /// Gets the estimated number of rows in the table.
    /// Null if row count is unknown.
    /// </summary>
    public long? EstimatedRowCount { get; init; }

    /// <summary>
    /// Gets the table creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the table last modification date.
    /// Null if modification date is unknown.
    /// </summary>
    public DateTime? ModifiedDate { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class.
    /// </summary>
    public Table()
    {
        // Parameterless constructor for serialization and object initialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class with required properties.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columns">The table columns.</param>
    public Table(string schemaName, string tableName, IEnumerable<Column> columns)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        Columns = columns?.ToList() ?? throw new ArgumentNullException(nameof(columns));
    }

    /// <summary>
    /// Gets the fully qualified table name (schema.table).
    /// </summary>
    public string FullyQualifiedName => $"{SchemaName}.{TableName}";

    /// <summary>
    /// Gets the number of columns in this table.
    /// </summary>
    public int ColumnCount => Columns.Count;

    /// <summary>
    /// Gets a value indicating whether this table has a primary key.
    /// </summary>
    public bool HasPrimaryKey => PrimaryKey is not null;

    /// <summary>
    /// Gets a value indicating whether this table has foreign keys.
    /// </summary>
    public bool HasForeignKeys => ForeignKeys.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this table has indexes (excluding primary key).
    /// </summary>
    public bool HasIndexes => Indexes.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this table has check constraints.
    /// </summary>
    public bool HasCheckConstraints => CheckConstraints.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this table has identity columns.
    /// </summary>
    public bool HasIdentityColumns => Columns.Any(c => c.IsIdentity);

    /// <summary>
    /// Gets the identity columns in this table.
    /// </summary>
    public IEnumerable<Column> IdentityColumns => Columns.Where(c => c.IsIdentity);

    /// <summary>
    /// Gets the nullable columns in this table.
    /// </summary>
    public IEnumerable<Column> NullableColumns => Columns.Where(c => c.IsNullable);

    /// <summary>
    /// Gets the non-nullable columns in this table.
    /// </summary>
    public IEnumerable<Column> NonNullableColumns => Columns.Where(c => !c.IsNullable);

    /// <summary>
    /// Finds a column by name (case-insensitive).
    /// </summary>
    /// <param name="columnName">The column name to find.</param>
    /// <returns>The column if found; otherwise, null.</returns>
    public Column? FindColumn(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return null;

        return Columns.FirstOrDefault(c =>
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all column names in this table.
    /// </summary>
    /// <returns>A list of column names.</returns>
    public List<string> GetColumnNames() => Columns.Select(c => c.Name).ToList();

    /// <summary>
    /// Gets the primary key column names.
    /// </summary>
    /// <returns>A list of primary key column names, or empty list if no primary key.</returns>
    public List<string> GetPrimaryKeyColumnNames() => PrimaryKey?.ColumnNames ?? [];

    /// <summary>
    /// Validates the table structure for consistency.
    /// </summary>
    /// <returns>A list of validation errors, or empty list if valid.</returns>
    public List<string> ValidateStructure()
    {
        var errors = new List<string>();

        // Basic validation
        if (string.IsNullOrWhiteSpace(SchemaName))
            errors.Add("Schema name cannot be empty");

        if (string.IsNullOrWhiteSpace(TableName))
            errors.Add("Table name cannot be empty");

        if (Columns.Count == 0)
            errors.Add("Table must have at least one column");

        // Column validation
        foreach (var column in Columns)
        {
            if (!column.IsValid())
                errors.Add($"Column '{column.Name}' has invalid properties");
        }

        // Check for duplicate column names
        var duplicateColumns = Columns
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicateColumns)
        {
            errors.Add($"Duplicate column name: '{duplicate}'");
        }

        // Primary key validation
        if (PrimaryKey is not null)
        {
            foreach (var pkColumn in PrimaryKey.ColumnNames)
            {
                if (FindColumn(pkColumn) is null)
                    errors.Add($"Primary key references non-existent column: '{pkColumn}'");
            }
        }

        // Foreign key validation
        foreach (var fk in ForeignKeys)
        {
            foreach (var fkColumn in fk.ColumnNames)
            {
                if (FindColumn(fkColumn) is null)
                    errors.Add($"Foreign key '{fk.Name}' references non-existent column: '{fkColumn}'");
            }
        }

        // Index validation
        foreach (var index in Indexes)
        {
            foreach (var indexColumn in index.ColumnNames)
            {
                if (FindColumn(indexColumn) is null)
                    errors.Add($"Index '{index.Name}' references non-existent column: '{indexColumn}'");
            }
        }

        // Default constraint validation
        foreach (var defaultConstraint in DefaultConstraints)
        {
            if (FindColumn(defaultConstraint.ColumnName) is null)
                errors.Add($"Default constraint '{defaultConstraint.Name}' references non-existent column: '{defaultConstraint.ColumnName}'");
        }

        return errors;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current table.
    /// </summary>
    /// <param name="obj">The object to compare with the current table.</param>
    /// <returns>True if the specified object is equal to the current table; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Table other &&
               SchemaName == other.SchemaName &&
               TableName == other.TableName &&
               Columns.SequenceEqual(other.Columns) &&
               Equals(PrimaryKey, other.PrimaryKey) &&
               ForeignKeys.SequenceEqual(other.ForeignKeys) &&
               Indexes.SequenceEqual(other.Indexes) &&
               CheckConstraints.SequenceEqual(other.CheckConstraints) &&
               DefaultConstraints.SequenceEqual(other.DefaultConstraints);
    }

    /// <summary>
    /// Returns the hash code for this table.
    /// </summary>
    /// <returns>A hash code for the current table.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaName);
        hash.Add(TableName);
        hash.Add(Columns.Count);
        hash.Add(PrimaryKey);
        hash.Add(ForeignKeys.Count);
        hash.Add(Indexes.Count);
        hash.Add(CheckConstraints.Count);
        hash.Add(DefaultConstraints.Count);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns a string representation of the table.
    /// </summary>
    /// <returns>A string that represents the current table.</returns>
    public override string ToString()
    {
        var pkInfo = HasPrimaryKey ? $" (PK: {string.Join(", ", GetPrimaryKeyColumnNames())})" : "";
        return $"{FullyQualifiedName} ({ColumnCount} columns{pkInfo})";
    }
}
