namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Input for storing a release artifact package. <see cref="Contents"/> is owned by the caller until passed to the store implementation.
/// </summary>
public sealed record ReleaseArtifactPut
{
    public required string PackageId { get; init; }

    public required string RetentionClass { get; init; }

    public required DateTimeOffset RetainUntilUtc { get; init; }

    public required Stream Contents { get; init; }

    /// <summary>
    /// Optional JSON sidecar metadata (manifest hash, build provenance). Must not contain secret material.
    /// </summary>
    public string? SidecarJson { get; init; }

    public string? BuiltByActorId { get; init; }
}
