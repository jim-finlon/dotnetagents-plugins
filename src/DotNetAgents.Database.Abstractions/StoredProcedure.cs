namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database stored procedure with its metadata and definition.
/// </summary>
public sealed class StoredProcedure
{
    /// <summary>
    /// Gets the schema name that contains this stored procedure.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets the name of the stored procedure.
    /// </summary>
    public required string ProcedureName { get; init; }

    /// <summary>
    /// Gets the SQL definition of the stored procedure.
    /// </summary>
    public required string Definition { get; init; }

    /// <summary>
    /// Gets the list of parameters for this stored procedure.
    /// </summary>
    public List<Parameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets the return type of the stored procedure.
    /// Null if the procedure doesn't return a value.
    /// </summary>
    public string? ReturnType { get; init; }

    /// <summary>
    /// Gets a value indicating whether this procedure is deterministic.
    /// </summary>
    public bool IsDeterministic { get; init; }

    /// <summary>
    /// Gets the stored procedure creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the stored procedure last modification date.
    /// Null if modification date is unknown.
    /// </summary>
    public DateTime? ModifiedDate { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredProcedure"/> class.
    /// </summary>
    public StoredProcedure()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredProcedure"/> class.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="procedureName">The procedure name.</param>
    /// <param name="definition">The procedure definition.</param>
    public StoredProcedure(string schemaName, string procedureName, string definition)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        ProcedureName = procedureName ?? throw new ArgumentNullException(nameof(procedureName));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>
    /// Gets the fully qualified procedure name (schema.procedure).
    /// </summary>
    public string FullyQualifiedName => $"{SchemaName}.{ProcedureName}";

    /// <summary>
    /// Gets the number of parameters in this procedure.
    /// </summary>
    public int ParameterCount => Parameters.Count;

    /// <summary>
    /// Gets the input parameters for this procedure.
    /// </summary>
    public IEnumerable<Parameter> InputParameters => Parameters.Where(p => p.Direction == ParameterDirection.Input);

    /// <summary>
    /// Gets the output parameters for this procedure.
    /// </summary>
    public IEnumerable<Parameter> OutputParameters => Parameters.Where(p => p.Direction == ParameterDirection.Output);

    /// <summary>
    /// Gets the input/output parameters for this procedure.
    /// </summary>
    public IEnumerable<Parameter> InputOutputParameters => Parameters.Where(p => p.Direction == ParameterDirection.InputOutput);

    /// <summary>
    /// Finds a parameter by name (case-insensitive).
    /// </summary>
    /// <param name="parameterName">The parameter name to find.</param>
    /// <returns>The parameter if found; otherwise, null.</returns>
    public Parameter? FindParameter(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return null;

        return Parameters.FirstOrDefault(p =>
            string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns a string representation of the stored procedure.
    /// </summary>
    /// <returns>A string that represents the current stored procedure.</returns>
    public override string ToString() => $"PROCEDURE {FullyQualifiedName} ({ParameterCount} parameters)";

    /// <summary>
    /// Determines whether the specified object is equal to the current stored procedure.
    /// </summary>
    /// <param name="obj">The object to compare with the current stored procedure.</param>
    /// <returns>True if the specified object is equal to the current stored procedure; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is StoredProcedure other &&
               SchemaName == other.SchemaName &&
               ProcedureName == other.ProcedureName &&
               Definition == other.Definition &&
               ReturnType == other.ReturnType &&
               IsDeterministic == other.IsDeterministic;
    }

    /// <summary>
    /// Returns the hash code for this stored procedure.
    /// </summary>
    /// <returns>A hash code for the current stored procedure.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(SchemaName, ProcedureName, Definition, ReturnType, IsDeterministic);
    }
}
