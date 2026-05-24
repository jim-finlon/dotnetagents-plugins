namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Backend-agnostic store for zipped release artifact packages (manifest, migrations, digests, rollback metadata).
/// Implementations (local filesystem, MinIO, S3) are responsible for persistence; callers must dispose streams from <see cref="OpenReadAsync"/>.
/// </summary>
public interface IReleaseArtifactStore
{
    Task<ReleaseArtifactDescriptor> PutAsync(ReleaseArtifactPut put, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a read stream for the stored package. The caller must dispose the returned <see cref="Stream"/>.
    /// </summary>
    Task<Stream> OpenReadAsync(string packageId, string retentionClass, CancellationToken cancellationToken);

    Task<ReleaseArtifactDescriptor?> GetDescriptorAsync(
        string packageId,
        string retentionClass,
        CancellationToken cancellationToken);

    IAsyncEnumerable<ReleaseArtifactDescriptor> ListExpiringBeforeAsync(
        DateTimeOffset utc,
        string? retentionClass,
        CancellationToken cancellationToken);

    Task<ReleaseArtifactDeletionReceipt> DeleteAsync(
        string packageId,
        string retentionClass,
        string deletionReason,
        CancellationToken cancellationToken);
}
