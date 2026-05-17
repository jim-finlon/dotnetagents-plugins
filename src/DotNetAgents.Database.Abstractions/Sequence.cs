namespace DotNetAgents.Database.Abstractions;

/// <summary>
/// Represents a database sequence with its metadata and properties.
/// </summary>
public sealed class Sequence
{
    /// <summary>
    /// Gets the schema name that contains this sequence.
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Gets the name of the sequence.
    /// </summary>
    public required string SequenceName { get; init; }

    /// <summary>
    /// Gets the data type of the sequence values.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Gets the starting value of the sequence.
    /// </summary>
    public long StartValue { get; init; } = 1;

    /// <summary>
    /// Gets the increment value for the sequence.
    /// </summary>
    public long IncrementBy { get; init; } = 1;

    /// <summary>
    /// Gets the minimum value of the sequence.
    /// Null if no minimum is specified.
    /// </summary>
    public long? MinValue { get; init; }

    /// <summary>
    /// Gets the maximum value of the sequence.
    /// Null if no maximum is specified.
    /// </summary>
    public long? MaxValue { get; init; }

    /// <summary>
    /// Gets the current value of the sequence.
    /// Null if current value is unknown.
    /// </summary>
    public long? CurrentValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether the sequence will cycle when it reaches its limits.
    /// </summary>
    public bool IsCycling { get; init; }

    /// <summary>
    /// Gets the cache size for the sequence.
    /// Null if no caching is used.
    /// </summary>
    public int? CacheSize { get; init; }

    /// <summary>
    /// Gets the sequence creation date.
    /// Null if creation date is unknown.
    /// </summary>
    public DateTime? CreatedDate { get; init; }

    /// <summary>
    /// Gets the sequence last modification date.
    /// Null if modification date is unknown.
    /// </summary>
    public DateTime? ModifiedDate { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sequence"/> class.
    /// </summary>
    public Sequence()
    {
        // Parameterless constructor for serialization
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sequence"/> class.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="sequenceName">The sequence name.</param>
    /// <param name="dataType">The sequence data type.</param>
    public Sequence(string schemaName, string sequenceName, string dataType)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
        SequenceName = sequenceName ?? throw new ArgumentNullException(nameof(sequenceName));
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
    }

    /// <summary>
    /// Gets the fully qualified sequence name (schema.sequence).
    /// </summary>
    public string FullyQualifiedName => $"{SchemaName}.{SequenceName}";

    /// <summary>
    /// Gets a value indicating whether this sequence has minimum/maximum limits defined.
    /// </summary>
    public bool HasLimits => MinValue.HasValue || MaxValue.HasValue;

    /// <summary>
    /// Gets a value indicating whether this sequence uses caching.
    /// </summary>
    public bool UsesCaching => CacheSize.HasValue && CacheSize > 1;

    /// <summary>
    /// Gets the range of values this sequence can produce.
    /// </summary>
    public (long Min, long Max) GetRange()
    {
        var min = MinValue ?? (DataType.ToUpperInvariant() switch
        {
            "tinyint" => 0,
            "smallint" => short.MinValue,
            "int" => int.MinValue,
            "bigint" => long.MinValue,
            _ => long.MinValue
        });

        var max = MaxValue ?? (DataType.ToUpperInvariant() switch
        {
            "tinyint" => byte.MaxValue,
            "smallint" => short.MaxValue,
            "int" => int.MaxValue,
            "bigint" => long.MaxValue,
            _ => long.MaxValue
        });

        return (min, max);
    }

    /// <summary>
    /// Validates the sequence properties for consistency.
    /// </summary>
    /// <returns>A list of validation errors, or empty list if valid.</returns>
    public List<string> ValidateProperties()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SchemaName))
            errors.Add("Schema name cannot be empty");

        if (string.IsNullOrWhiteSpace(SequenceName))
            errors.Add("Sequence name cannot be empty");

        if (string.IsNullOrWhiteSpace(DataType))
            errors.Add("Data type cannot be empty");

        if (IncrementBy == 0)
            errors.Add("Increment cannot be zero");

        var (rangeMin, rangeMax) = GetRange();

        if (StartValue < rangeMin || StartValue > rangeMax)
            errors.Add($"Start value {StartValue} is outside the valid range [{rangeMin}, {rangeMax}]");

        if (MinValue.HasValue && MaxValue.HasValue && MinValue >= MaxValue)
            errors.Add("Minimum value must be less than maximum value");

        if (CacheSize.HasValue && CacheSize <= 0)
            errors.Add("Cache size must be greater than zero");

        return errors;
    }

    /// <summary>
    /// Returns a string representation of the sequence.
    /// </summary>
    /// <returns>A string that represents the current sequence.</returns>
    public override string ToString()
    {
        var cycleStr = IsCycling ? " CYCLE" : " NO CYCLE";
        var cacheStr = UsesCaching ? $" CACHE {CacheSize}" : "";
        return $"SEQUENCE {FullyQualifiedName} START {StartValue} INCREMENT {IncrementBy}{cycleStr}{cacheStr}";
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current sequence.
    /// </summary>
    /// <param name="obj">The object to compare with the current sequence.</param>
    /// <returns>True if the specified object is equal to the current sequence; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Sequence other &&
               SchemaName == other.SchemaName &&
               SequenceName == other.SequenceName &&
               DataType == other.DataType &&
               StartValue == other.StartValue &&
               IncrementBy == other.IncrementBy &&
               MinValue == other.MinValue &&
               MaxValue == other.MaxValue &&
               IsCycling == other.IsCycling &&
               CacheSize == other.CacheSize;
    }

    /// <summary>
    /// Returns the hash code for this sequence.
    /// </summary>
    /// <returns>A hash code for the current sequence.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaName);
        hash.Add(SequenceName);
        hash.Add(DataType);
        hash.Add(StartValue);
        hash.Add(IncrementBy);
        hash.Add(MinValue);
        hash.Add(MaxValue);
        hash.Add(IsCycling);
        hash.Add(CacheSize);
        return hash.ToHashCode();
    }
}
