namespace DotNetAgents.Storage.ArtifactStore.Minio;

/// <summary>
/// Configuration for <see cref="MinioReleaseArtifactStore"/>.
/// </summary>
public sealed class MinioReleaseArtifactStoreOptions
{
    /// <summary>Target bucket (operator-provisioned; not auto-created).</summary>
    public string Bucket { get; set; } = "dna-release-artifacts";

    /// <summary>Multipart upload threshold in bytes (default 5 MiB).</summary>
    public long MultipartThresholdBytes { get; set; } = 5 * 1024 * 1024;
}
