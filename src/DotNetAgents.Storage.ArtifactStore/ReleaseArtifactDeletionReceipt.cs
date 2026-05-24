namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Audit receipt returned after a release artifact package is deleted from the store.
/// </summary>
public sealed record ReleaseArtifactDeletionReceipt
{
    public required string PackageId { get; init; }

    public required string RetentionClass { get; init; }

    public required DateTimeOffset DeletedAtUtc { get; init; }

    public string? DeletedByActorId { get; init; }

    public required string DeletionReason { get; init; }

    public required string StorageUri { get; init; }
}
