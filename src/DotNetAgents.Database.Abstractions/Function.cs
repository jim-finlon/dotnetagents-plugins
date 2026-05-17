namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database function with its metadata and definition.
/// </summary>
public sealed class Function
{
    /// <summary>
    /// Gets the schema name that contains this function.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets the name of the function.
    /// </summary>
    public required string FunctionName { get; init; }

    /// <summary>
    /// Gets the SQL definition of the function.
    /// </summary>
    public required string Definition { get; init; }

    /// <summary>
    /// Gets the return type of the function.
    /// </summary>
    public required string ReturnType { get; init; }

    /// <summary>
    /// Gets the list of parameters for this function.
    /// </summary>
    public List<Parameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets the type of function (scalar, table-valued, etc.).
    /// </summary>
    public FunctionType FunctionType { get; init; } = FunctionType.Scalar;

    /// <summary>
    /// Gets a value indicating whether this function is deterministic.
    /// </summary>
    public bool IsDeterministic { get; init; }

    /// <summary>
    /// Gets a value indicating whether this function accesses data.
    /// </summary>
    public bool AccessesData { get; init; }

    /// <summary>
    /// Gets the language used to implement the function (T-SQL, PL/pgSQL, etc.).
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the function creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the function last modification date.
    /// Null if modification date is unknown.
    /// </summary>
    public DateTime? ModifiedDate { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Function"/> class.
    /// </summary>
    public Function()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Function"/> class.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="functionName">The function name.</param>
    /// <param name="definition">The function definition.</param>
    /// <param name="returnType">The return type.</param>
    public Function(string schemaName, string functionName, string definition, string returnType)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ReturnType = returnType ?? throw new ArgumentNullException(nameof(returnType));
    }

    /// <summary>
    /// Gets the fully qualified function name (schema.function).
    /// </summary>
    public string FullyQualifiedName => $"{SchemaName}.{FunctionName}";

    /// <summary>
    /// Gets the number of parameters in this function.
    /// </summary>
    public int ParameterCount => Parameters.Count;

    /// <summary>
    /// Gets a value indicating whether this function returns a table.
    /// </summary>
    public bool ReturnsTable => FunctionType == FunctionType.TableValued;

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
    /// Returns a string representation of the function.
    /// </summary>
    /// <returns>A string that represents the current function.</returns>
    public override string ToString() =>
        $"{FunctionType.ToString().ToUpperInvariant()} FUNCTION {FullyQualifiedName} ({ParameterCount} parameters) -> {ReturnType}";

    /// <summary>
    /// Determines whether the specified object is equal to the current function.
    /// </summary>
    /// <param name="obj">The object to compare with the current function.</param>
    /// <returns>True if the specified object is equal to the current function; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Function other &&
               SchemaName == other.SchemaName &&
               FunctionName == other.FunctionName &&
               Definition == other.Definition &&
               ReturnType == other.ReturnType &&
               FunctionType == other.FunctionType &&
               IsDeterministic == other.IsDeterministic;
    }

    /// <summary>
    /// Returns the hash code for this function.
    /// </summary>
    /// <returns>A hash code for the current function.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaName);
        hash.Add(FunctionName);
        hash.Add(Definition);
        hash.Add(ReturnType);
        hash.Add(FunctionType);
        hash.Add(IsDeterministic);
        return hash.ToHashCode();
    }
}
