namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a parameter for a stored procedure or function.
/// </summary>
public sealed class Parameter
{
    /// <summary>
    /// Gets the name of the parameter.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the data type of the parameter.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Gets the direction of the parameter (input, output, input/output).
    /// </summary>
    public ParameterDirection Direction { get; init; } = ParameterDirection.Input;

    /// <summary>
    /// Gets the maximum length for character-based parameters.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the precision for numeric parameters.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// Gets the scale for numeric parameters.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    /// Gets the default value for the parameter.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether the parameter allows NULL values.
    /// </summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="Parameter"/> class.
    /// </summary>
    public Parameter()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Parameter"/> class.
    /// </summary>
    /// <param name="name">The parameter name.</param>
    /// <param name="dataType">The parameter data type.</param>
    /// <param name="direction">The parameter direction.</param>
    public Parameter(string name, string dataType, ParameterDirection direction = ParameterDirection.Input)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        Direction = direction;
    }

    /// <summary>
    /// Returns a string representation of the parameter.
    /// </summary>
    /// <returns>A string that represents the current parameter.</returns>
    public override string ToString()
    {
        var directionStr = Direction switch
        {
            ParameterDirection.Input => "IN",
            ParameterDirection.Output => "OUT",
            ParameterDirection.InputOutput => "INOUT",
            _ => ""
        };
        return $"{directionStr} {Name} {DataType}";
    }
}
