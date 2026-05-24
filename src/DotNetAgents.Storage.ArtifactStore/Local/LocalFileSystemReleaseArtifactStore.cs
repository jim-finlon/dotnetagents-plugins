using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAgents.Storage.ArtifactStore.Local;

/// <summary>
/// Filesystem-backed <see cref="IReleaseArtifactStore"/> using
/// <c>&lt;root&gt;/&lt;retentionClass&gt;/&lt;packageId&gt;/artifact.zip</c> and <c>sidecar.json</c>.
/// </summary>
public sealed class LocalFileSystemReleaseArtifactStore : IReleaseArtifactStore
{
    private const string ArtifactFileName = "artifact.zip";
    private const string SidecarFileName = "sidecar.json";
    private const string DeletionReceiptsFolder = "deletion-receipts";

    private static readonly JsonSerializerOptions SidecarJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly string _rootPath;
    private readonly ILogger<LocalFileSystemReleaseArtifactStore> _logger;

    public LocalFileSystemReleaseArtifactStore(
        IOptions<LocalFileSystemReleaseArtifactStoreOptions> options,
        ILogger<LocalFileSystemReleaseArtifactStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootPath = NormalizeRootPath(options.Value.RootPath);
    }

    /// <summary>
    /// Test and advanced-construction hook with an explicit root path.
    /// </summary>
    public LocalFileSystemReleaseArtifactStore(string rootPath, ILogger<LocalFileSystemReleaseArtifactStore> logger)
    {
        _rootPath = NormalizeRootPath(rootPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReleaseArtifactDescriptor> PutAsync(ReleaseArtifactPut put, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(put);
        ValidatePathSegment(put.PackageId, nameof(put.PackageId));
        ValidatePathSegment(put.RetentionClass, nameof(put.RetentionClass));

        var packageDirectory = GetPackageDirectory(put.RetentionClass, put.PackageId);
        await EnsurePackageDirectoryAsync(packageDirectory, cancellationToken).ConfigureAwait(false);

        var artifactPath = Path.Combine(packageDirectory, ArtifactFileName);
        var sidecarPath = Path.Combine(packageDirectory, SidecarFileName);
        var builtAtUtc = DateTimeOffset.UtcNow;
        var storageUri = BuildStorageUri(put.RetentionClass, put.PackageId);

        await WriteFileAtomicAsync(
            artifactPath,
            async destination =>
            {
                await put.Contents.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        var byteSize = new FileInfo(artifactPath).Length;

        var sidecar = new ReleaseArtifactSidecarDocument
        {
            PackageId = put.PackageId,
            RetentionClass = put.RetentionClass,
            RetainUntilUtc = put.RetainUntilUtc,
            BuiltAtUtc = builtAtUtc,
            BuiltByActorId = put.BuiltByActorId,
            ByteSize = byteSize,
            StorageUri = storageUri,
        };

        await WriteSidecarAtomicAsync(sidecarPath, sidecar, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Stored release artifact {PackageId} ({RetentionClass}) at {StorageUri}",
            put.PackageId,
            put.RetentionClass,
            storageUri);

        return ToDescriptor(sidecar);
    }

    public Task<Stream> OpenReadAsync(string packageId, string retentionClass, CancellationToken cancellationToken)
    {
        ValidatePathSegment(packageId, nameof(packageId));
        ValidatePathSegment(retentionClass, nameof(retentionClass));

        var artifactPath = Path.Combine(GetPackageDirectory(retentionClass, packageId), ArtifactFileName);
        if (!File.Exists(artifactPath))
        {
            throw new ReleaseArtifactNotFoundException(packageId, retentionClass);
        }

        Stream stream = new FileStream(
            artifactPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    public async Task<ReleaseArtifactDescriptor?> GetDescriptorAsync(
        string packageId,
        string retentionClass,
        CancellationToken cancellationToken)
    {
        ValidatePathSegment(packageId, nameof(packageId));
        ValidatePathSegment(retentionClass, nameof(retentionClass));

        var packageDirectory = GetPackageDirectory(retentionClass, packageId);
        var artifactPath = Path.Combine(packageDirectory, ArtifactFileName);
        var sidecarPath = Path.Combine(packageDirectory, SidecarFileName);

        if (!File.Exists(artifactPath) || !File.Exists(sidecarPath))
        {
            return null;
        }

        var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
        return sidecar is null ? null : ToDescriptor(sidecar);
    }

    public async IAsyncEnumerable<ReleaseArtifactDescriptor> ListExpiringBeforeAsync(
        DateTimeOffset utc,
        string? retentionClass,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        IEnumerable<string> retentionDirectories;
        if (!string.IsNullOrWhiteSpace(retentionClass))
        {
            ValidatePathSegment(retentionClass, nameof(retentionClass));
            var single = Path.Combine(_rootPath, retentionClass);
            retentionDirectories = Directory.Exists(single) ? [single] : Array.Empty<string>();
        }
        else
        {
            retentionDirectories = Directory.EnumerateDirectories(_rootPath)
                .Where(path => !string.Equals(Path.GetFileName(path), DeletionReceiptsFolder, StringComparison.Ordinal));
        }

        foreach (var retentionDirectory in retentionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var packageDirectory in Directory.EnumerateDirectories(retentionDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sidecarPath = Path.Combine(packageDirectory, SidecarFileName);
                var artifactPath = Path.Combine(packageDirectory, ArtifactFileName);
                if (!File.Exists(sidecarPath) || !File.Exists(artifactPath))
                {
                    continue;
                }

                var sidecar = await ReadSidecarAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
                if (sidecar is null || sidecar.RetainUntilUtc >= utc)
                {
                    continue;
                }

                yield return ToDescriptor(sidecar);
            }
        }
    }

    public async Task<ReleaseArtifactDeletionReceipt> DeleteAsync(
        string packageId,
        string retentionClass,
        string deletionReason,
        CancellationToken cancellationToken)
    {
        ValidatePathSegment(packageId, nameof(packageId));
        ValidatePathSegment(retentionClass, nameof(retentionClass));
        if (string.IsNullOrWhiteSpace(deletionReason))
        {
            throw new ArgumentException("Deletion reason is required.", nameof(deletionReason));
        }

        var descriptor = await GetDescriptorAsync(packageId, retentionClass, cancellationToken).ConfigureAwait(false)
            ?? throw new ReleaseArtifactNotFoundException(packageId, retentionClass);

        var deletedAtUtc = DateTimeOffset.UtcNow;
        var receipt = new ReleaseArtifactDeletionReceipt
        {
            PackageId = packageId,
            RetentionClass = retentionClass,
            DeletedAtUtc = deletedAtUtc,
            DeletedByActorId = null,
            DeletionReason = deletionReason,
            StorageUri = descriptor.StorageUri,
        };

        await WriteDeletionReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);

        var packageDirectory = GetPackageDirectory(retentionClass, packageId);
        var artifactPath = Path.Combine(packageDirectory, ArtifactFileName);
        var sidecarPath = Path.Combine(packageDirectory, SidecarFileName);

        if (File.Exists(artifactPath))
        {
            File.Delete(artifactPath);
        }

        if (File.Exists(sidecarPath))
        {
            File.Delete(sidecarPath);
        }

        if (Directory.Exists(packageDirectory) && !Directory.EnumerateFileSystemEntries(packageDirectory).Any())
        {
            Directory.Delete(packageDirectory);
        }

        _logger.LogInformation(
            "Deleted release artifact {PackageId} ({RetentionClass}); reason={DeletionReason}",
            packageId,
            retentionClass,
            deletionReason);

        return receipt;
    }

    private async Task WriteDeletionReceiptAsync(ReleaseArtifactDeletionReceipt receipt, CancellationToken cancellationToken)
    {
        var receiptsDirectory = Path.Combine(_rootPath, DeletionReceiptsFolder);
        Directory.CreateDirectory(receiptsDirectory);
        ApplyDirectoryPermissions(receiptsDirectory);

        var receiptPath = Path.Combine(receiptsDirectory, $"{Guid.NewGuid():N}.json");
        var json = JsonSerializer.Serialize(receipt, SidecarJsonOptions);
        await WriteFileAtomicAsync(
            receiptPath,
            async destination =>
            {
                await using var writer = new StreamWriter(destination, leaveOpen: true);
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteSidecarAtomicAsync(
        string sidecarPath,
        ReleaseArtifactSidecarDocument sidecar,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sidecar, SidecarJsonOptions);
        await WriteFileAtomicAsync(
            sidecarPath,
            async destination =>
            {
                await using var writer = new StreamWriter(destination, leaveOpen: true);
                await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteFileAtomicAsync(
        string targetPath,
        Func<Stream, Task> writeBody,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Invalid target path '{targetPath}'.");
        Directory.CreateDirectory(directory);

        var tempPath = targetPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        await using (var stream = new FileStream(
                         tempPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous))
        {
            await writeBody(stream).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
    }

    private static async Task<ReleaseArtifactSidecarDocument?> ReadSidecarAsync(
        string sidecarPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            sidecarPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await JsonSerializer.DeserializeAsync<ReleaseArtifactSidecarDocument>(stream, SidecarJsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private string GetPackageDirectory(string retentionClass, string packageId) =>
        Path.Combine(_rootPath, retentionClass, packageId);

    private string BuildStorageUri(string retentionClass, string packageId)
    {
        var artifactPath = Path.Combine(_rootPath, retentionClass, packageId, ArtifactFileName);
        return new Uri(artifactPath).AbsoluteUri;
    }

    private static ReleaseArtifactDescriptor ToDescriptor(ReleaseArtifactSidecarDocument sidecar) =>
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

    private static async Task EnsurePackageDirectoryAsync(string packageDirectory, CancellationToken cancellationToken)
    {
        if (Directory.Exists(packageDirectory))
        {
            return;
        }

        await Task.Run(
            () =>
            {
                Directory.CreateDirectory(packageDirectory);
                ApplyDirectoryPermissions(packageDirectory);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyDirectoryPermissions(string directoryPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            const UnixFileMode mode =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(directoryPath, mode);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new IOException(
                $"Unable to set permissions 0750 on artifact directory '{directoryPath}'. Ensure the process can write under the configured artifact root.",
                ex);
        }
    }

    private static string NormalizeRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Artifact store root path is required.", nameof(rootPath));
        }

        return Path.GetFullPath(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);
    }

    private static void ValidatePathSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", paramName);
        }

        if (value.Contains("..", StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Path segment contains invalid characters.", paramName);
        }
    }
}
