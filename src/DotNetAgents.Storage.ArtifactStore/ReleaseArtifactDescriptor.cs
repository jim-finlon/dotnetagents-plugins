namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Metadata describing a stored release artifact package.
/// </summary>
public sealed record ReleaseArtifactDescriptor
{
    public required string PackageId { get; init; }

    public required string RetentionClass { get; init; }

    public required DateTimeOffset RetainUntilUtc { get; init; }

    public required DateTimeOffset BuiltAtUtc { get; init; }

    public string? BuiltByActorId { get; init; }

    public required long ByteSize { get; init; }

    public string? SidecarJson { get; init; }

    public required string StorageUri { get; init; }
}
