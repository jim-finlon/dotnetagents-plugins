namespace DotNetAgents.Storage.ArtifactStore.Local;

/// <summary>
/// Configuration for <see cref="LocalFileSystemReleaseArtifactStore"/>.
/// </summary>
public sealed class LocalFileSystemReleaseArtifactStoreOptions
{
    /// <summary>
    /// Artifact root directory. Default matches Tyr Prime layout: /opt/dna/artifacts/prime/
    /// </summary>
    public string RootPath { get; set; } = "/opt/dna/artifacts/prime/";
}
