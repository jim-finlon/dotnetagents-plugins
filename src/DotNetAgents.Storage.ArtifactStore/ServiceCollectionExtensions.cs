using DotNetAgents.Storage.ArtifactStore.Local;
using DotNetAgents.Storage.ArtifactStore.Minio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Storage.ArtifactStore;

/// <summary>
/// DI registration for <see cref="IReleaseArtifactStore"/> backends.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IReleaseArtifactStore"/> using configuration under <c>DotNetAgents:ArtifactStore</c>.
    /// When <c>DotNetAgents:ArtifactStore:Backend</c> is <c>local</c> (default), uses <see cref="LocalFileSystemReleaseArtifactStore"/>.
    /// </summary>
    public static IServiceCollection AddDnaReleaseArtifactStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<LocalFileSystemReleaseArtifactStoreOptions>(
            configuration.GetSection("DotNetAgents:ArtifactStore:Local"));
        services.Configure<MinioReleaseArtifactStoreOptions>(
            configuration.GetSection("DotNetAgents:ArtifactStore:Minio"));

        var backend = configuration["DotNetAgents:ArtifactStore:Backend"];
        if (string.Equals(backend, "minio", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IMinioReleaseArtifactStoreCredentialSource, CredentialReferenceMinioCredentialSource>();
            services.AddSingleton<IReleaseArtifactStore, MinioReleaseArtifactStore>();
        }
        else if (string.IsNullOrWhiteSpace(backend) || string.Equals(backend, "local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IReleaseArtifactStore, LocalFileSystemReleaseArtifactStore>();
        }

        return services;
    }
}
