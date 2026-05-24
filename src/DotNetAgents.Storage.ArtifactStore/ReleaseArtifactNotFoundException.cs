namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// Thrown when a release artifact package cannot be found for the given id and retention class.
/// </summary>
public sealed class ReleaseArtifactNotFoundException : Exception
{
    public ReleaseArtifactNotFoundException(string packageId, string retentionClass)
        : base($"Release artifact package '{packageId}' was not found for retention class '{retentionClass}'.")
    {
        PackageId = packageId;
        RetentionClass = retentionClass;
    }

    public ReleaseArtifactNotFoundException(string packageId, string retentionClass, Exception innerException)
        : base(
            $"Release artifact package '{packageId}' was not found for retention class '{retentionClass}'.",
            innerException)
    {
        PackageId = packageId;
        RetentionClass = retentionClass;
    }

    public string PackageId { get; }

    public string RetentionClass { get; }
}
