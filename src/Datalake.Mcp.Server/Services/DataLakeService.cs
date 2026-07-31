using Azure.Storage.Files.DataLake;
using Azure.Storage.Files.DataLake.Models;
using Datalake.Mcp.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Datalake.Mcp.Server.Services;

public sealed record DataLakePathInfo(string Name, bool IsDirectory, long? Length, DateTimeOffset? LastModified);

/// <summary>
/// Wraps <see cref="DataLakeServiceClient"/> to expose navigation and streaming primitives over the
/// configured Azure Data Lake Gen2 account. Clients are cached per filesystem to avoid re-authenticating
/// on every call.
/// </summary>
public sealed class DataLakeService
{
    private readonly DataLakeServiceClient _serviceClient;
    private readonly DataLakeOptions _options;

    public DataLakeService(IOptions<DataLakeOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.AccountName) || string.IsNullOrWhiteSpace(_options.AccountKey))
        {
            throw new InvalidOperationException(
                "DataLake:AccountName e DataLake:AccountKey devono essere configurati (appsettings, variabili d'ambiente o user-secrets).");
        }

        var serviceUri = new Uri($"https://{_options.AccountName}.dfs.core.windows.net");
        var credential = new Azure.Storage.StorageSharedKeyCredential(_options.AccountName, _options.AccountKey);
        _serviceClient = new DataLakeServiceClient(serviceUri, credential);
    }

    public string ResolveFileSystem(string? fileSystem)
    {
        var resolved = fileSystem ?? _options.DefaultFileSystem;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new ArgumentException(
                "Nessun filesystem specificato e nessun DataLake:DefaultFileSystem configurato.", nameof(fileSystem));
        }

        return resolved;
    }

    public async Task<IReadOnlyList<string>> ListFileSystemsAsync(CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await foreach (FileSystemItem item in _serviceClient.GetFileSystemsAsync(cancellationToken: cancellationToken))
        {
            names.Add(item.Name);
        }

        return names;
    }

    public async Task<IReadOnlyList<DataLakePathInfo>> ListDirectoryAsync(
        string fileSystem, string path, bool recursive, CancellationToken cancellationToken)
    {
        var fileSystemClient = _serviceClient.GetFileSystemClient(fileSystem);
        var results = new List<DataLakePathInfo>();

        var options = new DataLakeGetPathsOptions { Path = path, Recursive = recursive };
        await foreach (PathItem item in fileSystemClient.GetPathsAsync(options, cancellationToken))
        {
            results.Add(new DataLakePathInfo(item.Name, item.IsDirectory ?? false, item.ContentLength, item.LastModified));
        }

        return results;
    }

    public async Task<DataLakePathInfo> GetFileInfoAsync(string fileSystem, string path, CancellationToken cancellationToken)
    {
        var fileClient = _serviceClient.GetFileSystemClient(fileSystem).GetFileClient(path);
        var properties = await fileClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        return new DataLakePathInfo(path, false, properties.Value.ContentLength, properties.Value.LastModified);
    }

    /// <summary>
    /// Opens a lazily-loading, seekable stream over the remote file: bytes are fetched on demand via ranged
    /// requests as the caller reads/seeks, instead of downloading the whole blob up front. This is what makes
    /// columnar Parquet reads (footer + selected column chunks only) efficient over the network.
    /// </summary>
    public Task<Stream> OpenReadAsync(string fileSystem, string path, CancellationToken cancellationToken)
    {
        var fileClient = _serviceClient.GetFileSystemClient(fileSystem).GetFileClient(path);
        return fileClient.OpenReadAsync(new DataLakeOpenReadOptions(allowModifications: false), cancellationToken);
    }
}
