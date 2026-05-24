namespace DotNetAgents.Storage.ArtifactStore.Minio;

/// <summary>
/// Resolves MinIO endpoint and keys via CredentialsAgent-backed credential references.
/// </summary>
public interface IMinioReleaseArtifactStoreCredentialSource
{
    Task<MinioConnectionSettings> GetConnectionSettingsAsync(CancellationToken cancellationToken);
}
