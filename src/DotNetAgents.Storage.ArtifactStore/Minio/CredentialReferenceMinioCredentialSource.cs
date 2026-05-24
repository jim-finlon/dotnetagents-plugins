using System.Text;
using DotNetAgents.Abstractions.PublicSubstitutes.Credentials;

namespace DotNetAgents.Storage.ArtifactStore.Minio;

/// <summary>
/// Resolves <c>dna-storage-minio</c> category references through <see cref="ICredentialReferenceResolver"/>.
/// </summary>
public sealed class CredentialReferenceMinioCredentialSource : IMinioReleaseArtifactStoreCredentialSource
{
    public const string CredentialCategory = "dna-storage-minio";
    public const string EndpointName = "endpoint";
    public const string AccessKeyName = "access-key";
    public const string SecretKeyName = "secret-key";

    private readonly ICredentialReferenceResolver _credentialResolver;

    public CredentialReferenceMinioCredentialSource(ICredentialReferenceResolver credentialResolver)
    {
        _credentialResolver = credentialResolver ?? throw new ArgumentNullException(nameof(credentialResolver));
    }

    public async Task<MinioConnectionSettings> GetConnectionSettingsAsync(CancellationToken cancellationToken)
    {
        var endpoint = await ReadCredentialAsync(EndpointName, cancellationToken).ConfigureAwait(false);
        var accessKey = await ReadCredentialAsync(AccessKeyName, cancellationToken).ConfigureAwait(false);
        var secretKey = await ReadCredentialAsync(SecretKeyName, cancellationToken).ConfigureAwait(false);

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException(
                "MinIO endpoint credential must be an absolute URL (for example https://minio.example:9000).");
        }

        return new MinioConnectionSettings(endpointUri, accessKey, secretKey);
    }

    private async Task<string> ReadCredentialAsync(string name, CancellationToken cancellationToken)
    {
        await using var accessor = await _credentialResolver
            .ResolveAsync(new CredentialReference(CredentialCategory, name), cancellationToken)
            .ConfigureAwait(false);

        var view = await accessor.AccessAsync(cancellationToken).ConfigureAwait(false);
        return view.Value.ToString();
    }
}
