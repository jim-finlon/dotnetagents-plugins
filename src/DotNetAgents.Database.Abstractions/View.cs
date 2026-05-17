namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database view with its metadata and definition.
/// </summary>
public sealed class View
{
    /// <summary>
    /// Gets the schema name that contains this view.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets the name of the view.
    /// </summary>
    public required string ViewName { get; init; }

    /// <summary>
    /// Gets the SQL definition of the view.
    /// </summary>
    public required string Definition { get; init; }

    /// <summary>
    /// Gets the list of columns in this view.
    /// </summary>
    public List<Column> Columns { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this view is updatable.
    /// </summary>
    public bool IsUpdatable { get; init; }

    /// <summary>
    /// Gets a value indicating whether this view has check option enabled.
    /// </summary>
    public bool HasCheckOption { get; init; }

    /// <summary>
    /// Gets the view creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the view last modification date.
    /// Null if modification date is unknown.
    /// </summary>
    public DateTime? ModifiedDate { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="View"/> class.
    /// </summary>
    public View()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="View"/> class.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="viewName">The view name.</param>
    /// <param name="definition">The view definition.</param>
    public View(string schemaName, string viewName, string definition)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        ViewName = viewName ?? throw new ArgumentNullException(nameof(viewName));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>
    /// Gets the fully qualified view name (schema.view).
    /// </summary>
    public string FullyQualifiedName => $"{SchemaName}.{ViewName}";

    /// <summary>
    /// Gets the number of columns in this view.
    /// </summary>
    public int ColumnCount => Columns.Count;

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
    /// Gets all column names in this view.
    /// </summary>
    /// <returns>A list of column names.</returns>
    public List<string> GetColumnNames() => Columns.Select(c => c.Name).ToList();

    /// <summary>
    /// Returns a string representation of the view.
    /// </summary>
    /// <returns>A string that represents the current view.</returns>
    public override string ToString() => $"VIEW {FullyQualifiedName} ({ColumnCount} columns)";

    /// <summary>
    /// Determines whether the specified object is equal to the current view.
    /// </summary>
    /// <param name="obj">The object to compare with the current view.</param>
    /// <returns>True if the specified object is equal to the current view; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is View other &&
               SchemaName == other.SchemaName &&
               ViewName == other.ViewName &&
               Definition == other.Definition &&
               IsUpdatable == other.IsUpdatable &&
               HasCheckOption == other.HasCheckOption;
    }

    /// <summary>
    /// Returns the hash code for this view.
    /// </summary>
    /// <returns>A hash code for the current view.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(SchemaName, ViewName, Definition, IsUpdatable, HasCheckOption);
    }
}
