namespace DotNetAgents.Storage.ArtifactStore.Minio;

/// <summary>
/// MinIO connection settings resolved from CredentialsAgent (never logged).
/// </summary>
public sealed record MinioConnectionSettings(Uri Endpoint, string AccessKey, string SecretKey);
