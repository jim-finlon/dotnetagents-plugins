using System.Text.Json;

namespace DotNetAgents.Storage.ArtifactStore;

internal static class ReleaseArtifactSidecarJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    internal static async Task<ReleaseArtifactSidecarDocument?> ReadAsync(Stream stream, CancellationToken cancellationToken) =>
        await JsonSerializer.DeserializeAsync<ReleaseArtifactSidecarDocument>(stream, Options, cancellationToken).ConfigureAwait(false);

    internal static async Task WriteAsync(Stream stream, ReleaseArtifactSidecarDocument sidecar, CancellationToken cancellationToken) =>
        await JsonSerializer.SerializeAsync(stream, sidecar, Options, cancellationToken).ConfigureAwait(false);

    internal static ReleaseArtifactDescriptor ToDescriptor(ReleaseArtifactSidecarDocument sidecar) =>
        new()
        {
            PackageId = sidecar.PackageId,
            RetentionClass = sidecar.RetentionClass,
            RetainUntilUtc = sidecar.RetainUntilUtc,
            BuiltAtUtc = sidecar.BuiltAtUtc,
            BuiltByActorId = sidecar.BuiltByActorId,
            ByteSize = sidecar.ByteSize,
            SidecarJson = null,
            StorageUri = sidecar.StorageUri,
        };
}
