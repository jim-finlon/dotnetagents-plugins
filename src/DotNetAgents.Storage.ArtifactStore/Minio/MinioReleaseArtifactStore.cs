using System.Runtime.CompilerServices;
using System.Text.Json;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAgents.Storage.ArtifactStore.Minio;

/// <summary>
/// S3-compatible <see cref="IReleaseArtifactStore"/> backed by MinIO.
/// </summary>
public sealed class MinioReleaseArtifactStore : IReleaseArtifactStore
{
    private const string ArtifactObjectName = "artifact.zip";
    private const string SidecarObjectName = "sidecar.json";
    private const string DeletionReceiptsPrefix = "deletion-receipts/";

    private readonly IMinioReleaseArtifactStoreCredentialSource _credentialSource;
    private readonly MinioReleaseArtifactStoreOptions _options;
    private readonly ILogger<MinioReleaseArtifactStore> _logger;

    public MinioReleaseArtifactStore(
        IMinioReleaseArtifactStoreCredentialSource credentialSource,
        IOptions<MinioReleaseArtifactStoreOptions> options,
        ILogger<MinioReleaseArtifactStore> logger)
    {
        _credentialSource = credentialSource ?? throw new ArgumentNullException(nameof(credentialSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReleaseArtifactDescriptor> PutAsync(ReleaseArtifactPut put, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(put);
        ValidateObjectSegment(put.PackageId, nameof(put.PackageId));
        ValidateObjectSegment(put.RetentionClass, nameof(put.RetentionClass));

        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        await EnsureBucketExistsAsync(client, cancellationToken).ConfigureAwait(false);

        var artifactKey = BuildObjectKey(put.RetentionClass, put.PackageId, ArtifactObjectName);
        var sidecarKey = BuildObjectKey(put.RetentionClass, put.PackageId, SidecarObjectName);
        var builtAtUtc = DateTimeOffset.UtcNow;
        var storageUri = BuildStorageUri(_options.Bucket, artifactKey);

        var (payload, byteSize) = await MaterializeStreamAsync(put.Contents, cancellationToken).ConfigureAwait(false);

        await ExecuteWithRetryAsync(
            async ct =>
            {
                await using var uploadStream = new MemoryStream(payload, writable: false);
                await client.PutObjectAsync(
                    new PutObjectArgs()
                        .WithBucket(_options.Bucket)
                        .WithObject(artifactKey)
                        .WithStreamData(uploadStream)
                        .WithObjectSize(byteSize),
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

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

        await PutSidecarAsync(client, sidecarKey, sidecar, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Stored release artifact {PackageId} ({RetentionClass}) in bucket {Bucket}",
            put.PackageId,
            put.RetentionClass,
            _options.Bucket);

        return ReleaseArtifactSidecarJson.ToDescriptor(sidecar);
    }

    public async Task<Stream> OpenReadAsync(string packageId, string retentionClass, CancellationToken cancellationToken)
    {
        ValidateObjectSegment(packageId, nameof(packageId));
        ValidateObjectSegment(retentionClass, nameof(retentionClass));

        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var artifactKey = BuildObjectKey(retentionClass, packageId, ArtifactObjectName);

        try
        {
            var memoryStream = new MemoryStream();
            await client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(artifactKey)
                    .WithCallbackStream(
                        async (source, ct) => await source.CopyToAsync(memoryStream, ct).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (ObjectNotFoundException)
        {
            throw new ReleaseArtifactNotFoundException(packageId, retentionClass);
        }
    }

    public async Task<ReleaseArtifactDescriptor?> GetDescriptorAsync(
        string packageId,
        string retentionClass,
        CancellationToken cancellationToken)
    {
        ValidateObjectSegment(packageId, nameof(packageId));
        ValidateObjectSegment(retentionClass, nameof(retentionClass));

        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var artifactKey = BuildObjectKey(retentionClass, packageId, ArtifactObjectName);
        var sidecarKey = BuildObjectKey(retentionClass, packageId, SidecarObjectName);

        if (!await ObjectExistsAsync(client, artifactKey, cancellationToken).ConfigureAwait(false)
            || !await ObjectExistsAsync(client, sidecarKey, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var sidecar = await GetSidecarAsync(client, sidecarKey, cancellationToken).ConfigureAwait(false);
        return sidecar is null ? null : ReleaseArtifactSidecarJson.ToDescriptor(sidecar);
    }

    public async IAsyncEnumerable<ReleaseArtifactDescriptor> ListExpiringBeforeAsync(
        DateTimeOffset utc,
        string? retentionClass,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        await EnsureBucketExistsAsync(client, cancellationToken).ConfigureAwait(false);

        var listPrefix = string.IsNullOrWhiteSpace(retentionClass)
            ? string.Empty
            : retentionClass.TrimEnd('/') + "/";

        if (!string.IsNullOrWhiteSpace(retentionClass))
        {
            ValidateObjectSegment(retentionClass, nameof(retentionClass));
        }

        var listArgs = new ListObjectsArgs()
            .WithBucket(_options.Bucket)
            .WithPrefix(listPrefix)
            .WithRecursive(true);

        await foreach (var item in client.ListObjectsEnumAsync(listArgs, cancellationToken).ConfigureAwait(false))
        {
            if (item.Key.StartsWith(DeletionReceiptsPrefix, StringComparison.Ordinal)
                || !item.Key.EndsWith(SidecarObjectName, StringComparison.Ordinal))
            {
                continue;
            }

            var sidecar = await GetSidecarAsync(client, item.Key, cancellationToken).ConfigureAwait(false);
            if (sidecar is null || sidecar.RetainUntilUtc >= utc)
            {
                continue;
            }

            var artifactKey = item.Key.Replace(SidecarObjectName, ArtifactObjectName, StringComparison.Ordinal);
            if (!await ObjectExistsAsync(client, artifactKey, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            yield return ReleaseArtifactSidecarJson.ToDescriptor(sidecar);
        }
    }

    public async Task<ReleaseArtifactDeletionReceipt> DeleteAsync(
        string packageId,
        string retentionClass,
        string deletionReason,
        CancellationToken cancellationToken)
    {
        ValidateObjectSegment(packageId, nameof(packageId));
        ValidateObjectSegment(retentionClass, nameof(retentionClass));
        if (string.IsNullOrWhiteSpace(deletionReason))
        {
            throw new ArgumentException("Deletion reason is required.", nameof(deletionReason));
        }

        var descriptor = await GetDescriptorAsync(packageId, retentionClass, cancellationToken).ConfigureAwait(false)
            ?? throw new ReleaseArtifactNotFoundException(packageId, retentionClass);

        var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
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

        var receiptKey = $"{DeletionReceiptsPrefix}{Guid.NewGuid():N}.json";
        await PutJsonObjectAsync(client, receiptKey, receipt, cancellationToken).ConfigureAwait(false);

        var artifactKey = BuildObjectKey(retentionClass, packageId, ArtifactObjectName);
        var sidecarKey = BuildObjectKey(retentionClass, packageId, SidecarObjectName);

        await ExecuteWithRetryAsync(
            async ct =>
            {
                await client.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(_options.Bucket).WithObject(artifactKey),
                    ct).ConfigureAwait(false);
                await client.RemoveObjectAsync(
                    new RemoveObjectArgs().WithBucket(_options.Bucket).WithObject(sidecarKey),
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Deleted release artifact {PackageId} ({RetentionClass}) from bucket {Bucket}; reason={DeletionReason}",
            packageId,
            retentionClass,
            _options.Bucket,
            deletionReason);

        return receipt;
    }

    private async Task PutSidecarAsync(
        IMinioClient client,
        string sidecarKey,
        ReleaseArtifactSidecarDocument sidecar,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await ReleaseArtifactSidecarJson.WriteAsync(buffer, sidecar, cancellationToken).ConfigureAwait(false);
        var payload = buffer.ToArray();

        await ExecuteWithRetryAsync(
            async ct =>
            {
                await using var upload = new MemoryStream(payload, writable: false);
                await client.PutObjectAsync(
                    new PutObjectArgs()
                        .WithBucket(_options.Bucket)
                        .WithObject(sidecarKey)
                        .WithStreamData(upload)
                        .WithObjectSize(payload.Length),
                    ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PutJsonObjectAsync<T>(IMinioClient client, string objectKey, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, ReleaseArtifactSidecarJson.Options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await using var stream = new MemoryStream(bytes, writable: false);
        await client.PutObjectAsync(
            new PutObjectArgs()
                .WithBucket(_options.Bucket)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReleaseArtifactSidecarDocument?> GetSidecarAsync(
        IMinioClient client,
        string sidecarKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var buffer = new MemoryStream();
            await client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(sidecarKey)
                    .WithCallbackStream(
                        async (stream, ct) => await stream.CopyToAsync(buffer, ct).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);

            buffer.Position = 0;
            return await ReleaseArtifactSidecarJson.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    private async Task<bool> ObjectExistsAsync(IMinioClient client, string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await client.StatObjectAsync(
                new StatObjectArgs().WithBucket(_options.Bucket).WithObject(objectKey),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    private async Task EnsureBucketExistsAsync(IMinioClient client, CancellationToken cancellationToken)
    {
        var exists = await client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_options.Bucket),
            cancellationToken).ConfigureAwait(false);

        if (!exists)
        {
            throw new InvalidOperationException(
                $"MinIO bucket '{_options.Bucket}' is not provisioned. Create the bucket before enabling Backend=minio.");
        }
    }

    private async Task<IMinioClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var settings = await _credentialSource.GetConnectionSettingsAsync(cancellationToken).ConfigureAwait(false);
        var port = settings.Endpoint.Port > 0
            ? settings.Endpoint.Port
            : settings.Endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 9000;

        var builder = new MinioClient()
            .WithEndpoint(settings.Endpoint.Host, port)
            .WithCredentials(settings.AccessKey, settings.SecretKey);

        if (settings.Endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithSSL();
        }

        return builder.Build();
    }

    private static string BuildObjectKey(string retentionClass, string packageId, string fileName) =>
        $"{retentionClass}/{packageId}/{fileName}";

    private static string BuildStorageUri(string bucket, string objectKey) =>
        $"s3://{bucket}/{objectKey}";

    private static async Task<(byte[] Payload, long ByteSize)> MaterializeStreamAsync(
        Stream contents,
        CancellationToken cancellationToken)
    {
        if (contents.CanSeek)
        {
            contents.Position = 0;
            var length = contents.Length - contents.Position;
            var buffer = new byte[length];
            var read = 0;
            while (read < length)
            {
                read += await contents.ReadAsync(buffer.AsMemory(read, (int)length - read), cancellationToken).ConfigureAwait(false);
            }

            return (buffer, length);
        }

        await using var memory = new MemoryStream();
        await contents.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return (memory.ToArray(), memory.Length);
    }

    private static async Task ExecuteWithRetryAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromMilliseconds(100);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < 2 && IsTransient(ex))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay;
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or IOException or MinioException;

    private static void ValidateObjectSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", paramName);
        }

        if (value.Contains("..", StringComparison.Ordinal)
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("Object key segment contains invalid characters.", paramName);
        }
    }
}
