namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// JSON sidecar persisted beside <c>artifact.zip</c> for list-expiring and descriptor queries.
/// </summary>
internal sealed class ReleaseArtifactSidecarDocument
{
    public required string PackageId { get; init; }

    public required string RetentionClass { get; init; }

    public required DateTimeOffset RetainUntilUtc { get; init; }

    public required DateTimeOffset BuiltAtUtc { get; init; }

    public string? BuiltByActorId { get; init; }

    public required long ByteSize { get; init; }

    public required string StorageUri { get; init; }
}
