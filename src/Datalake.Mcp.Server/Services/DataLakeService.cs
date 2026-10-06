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
    public static string NormalizePath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            throw new ArgumentException("Percorso file non valido: serve un nome file, senza segmenti '.' o '..'.", nameof(path));
        }

        return string.Join('/', segments);
    }

    /// <summary>
    /// Uploads <paramref name="content"/> to <paramref name="path"/> (missing directories are created by Gen2)
    /// and registers it in the saved-files index. Without <paramref name="overwrite"/> an existing file is never
    /// replaced: the upload is conditional (If-None-Match: *), so concurrent writers cannot clobber each other.
    /// </summary>
    public async Task<FileIndexEntry> SaveFileAsync(
        string fileSystem, string path, byte[] content, string format, string? description, bool overwrite,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path);
        var indexPath = NormalizePath(_options.SavedFilesIndexPath);
        if (string.Equals(normalizedPath, indexPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Il percorso coincide con il file di indice e non può essere sovrascritto.", nameof(path));
        }

        var fileSystemClient = _serviceClient.GetFileSystemClient(fileSystem);
        var fileClient = fileSystemClient.GetFileClient(normalizedPath);

        using (var data = new MemoryStream(content, writable: false))
        {
            try
            {
                await fileClient.UploadAsync(
                    data,
                    new DataLakeFileUploadOptions
                    {
                        Conditions = overwrite ? null : new DataLakeRequestConditions { IfNoneMatch = Azure.ETag.All },
                    },
                    cancellationToken);
            }
            catch (Azure.RequestFailedException ex) when (!overwrite && ex.Status is 409 or 412)
            {
                throw new InvalidOperationException(
                    $"Il file '{normalizedPath}' esiste già: usa overwrite=true per sostituirlo.", ex);
            }
        }

        var entry = new FileIndexEntry(
            normalizedPath, normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..], format, content.Length,
            DateTimeOffset.UtcNow, description);

        await UpdateIndexAsync(fileSystemClient.GetFileClient(indexPath), entry, cancellationToken);
        return entry;
    }

    /// <summary>Searches the saved-files index by name ("contains") and/or format; an absent index yields no results.</summary>
    public async Task<IReadOnlyList<FileIndexEntry>> SearchFilesAsync(
        string fileSystem, string? name, string? format, CancellationToken cancellationToken)
    {
        var indexClient = _serviceClient.GetFileSystemClient(fileSystem).GetFileClient(NormalizePath(_options.SavedFilesIndexPath));
        try
        {
            var download = await indexClient.ReadContentAsync(cancellationToken);
            return FileIndex.Search(FileIndex.Parse(download.Value.Content.ToString()), name, format);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return [];
        }
    }

    // Read-modify-write guarded by the index's ETag; on a concurrent update the loop re-reads and retries.
    private static async Task UpdateIndexAsync(DataLakeFileClient indexClient, FileIndexEntry entry, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            string? existing = null;
            Azure.ETag? etag = null;
            try
            {
                var download = await indexClient.ReadContentAsync(cancellationToken);
                existing = download.Value.Content.ToString();
                etag = download.Value.Details.ETag;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
            }

            var bytes = new System.Text.UTF8Encoding(false).GetBytes(FileIndex.Upsert(existing, entry));
            using var data = new MemoryStream(bytes, writable: false);
            try
            {
                await indexClient.UploadAsync(
                    data,
                    new DataLakeFileUploadOptions
                    {
                        Conditions = new DataLakeRequestConditions
                        {
                            IfMatch = etag,
                            IfNoneMatch = etag is null ? Azure.ETag.All : null,
                        },
                    },
                    cancellationToken);
                return;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < maxAttempts)
            {
            }
        }
    }

    public Task<Stream> OpenReadAsync(string fileSystem, string path, CancellationToken cancellationToken)
    {
        var fileClient = _serviceClient.GetFileSystemClient(fileSystem).GetFileClient(path);
        return fileClient.OpenReadAsync(new DataLakeOpenReadOptions(allowModifications: false), cancellationToken);
    }
}
