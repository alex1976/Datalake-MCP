using System.ComponentModel;
using Datalake.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Datalake.Mcp.Server.Tools;

[McpServerToolType]
public sealed class DataLakeTools(
    DataLakeService dataLake,
    CsvReaderService csvReader,
    ParquetReaderService parquetReader,
    TextReaderService textReader)
{
    private const int DefaultMaxRows = 200;
    private const int HardMaxRows = 10_000;

    [McpServerTool(Name = "list_filesystems"), Description(
        "Elenca i filesystem (container) disponibili nell'account Azure Data Lake Gen2 configurato.")]
    public async Task<IReadOnlyList<string>> ListFileSystemsAsync(CancellationToken cancellationToken)
        => await dataLake.ListFileSystemsAsync(cancellationToken);

    [McpServerTool(Name = "list_directory"), Description(
        "Elenca file e directory presenti in un percorso del datalake. Usa 'recursive' con cautela su alberi molto grandi.")]
    public async Task<IReadOnlyList<DataLakePathInfo>> ListDirectoryAsync(
        [Description("Percorso della directory da elencare, relativo alla radice del filesystem. Vuoto per la radice.")]
        string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Se true, elenca ricorsivamente tutte le sottodirectory.")]
        bool recursive = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        return await dataLake.ListDirectoryAsync(resolvedFileSystem, path, recursive, cancellationToken);
    }

    [McpServerTool(Name = "get_file_info"), Description("Restituisce metadati (dimensione, ultima modifica) di un file del datalake.")]
    public async Task<DataLakePathInfo> GetFileInfoAsync(
        [Description("Percorso completo del file all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        return await dataLake.GetFileInfoAsync(resolvedFileSystem, path, cancellationToken);
    }

    [McpServerTool(Name = "read_csv"), Description(
        "Legge un file CSV dal datalake restituendo colonne e righe. La lettura si interrompe non appena " +
        "'maxRows' viene raggiunto, per non caricare file di grandi dimensioni in memoria inutilmente. " +
        "Se il risultato ha 'truncated' true, richiama nuovamente il tool passando 'offset' = 'nextOffset' " +
        "per leggere la pagina successiva.")]
    public async Task<CsvReadResult> ReadCsvAsync(
        [Description("Percorso completo del file CSV all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe da restituire (default 200, massimo 10000).")]
        int maxRows = DefaultMaxRows,
        [Description("Carattere separatore di campo.")] string delimiter = ",",
        [Description("Indica se la prima riga contiene i nomi delle colonne.")] bool hasHeader = true,
        [Description("Numero di righe dati da saltare prima di iniziare a restituire risultati, per leggere pagine successive di un file di grandi dimensioni.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await csvReader.ReadAsync(stream, boundedMaxRows, delimiter, hasHeader, boundedOffset, cancellationToken);
    }

    [McpServerTool(Name = "get_parquet_schema"), Description(
        "Restituisce lo schema (nomi colonna e tipi) di un file Parquet senza leggerne i dati, utile per decidere " +
        "quali colonne proiettare con read_parquet.")]
    public async Task<IReadOnlyList<ParquetColumnInfo>> GetParquetSchemaAsync(
        [Description("Percorso completo del file Parquet all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await parquetReader.GetSchemaAsync(stream, cancellationToken);
    }

    [McpServerTool(Name = "read_parquet"), Description(
        "Legge un file Parquet dal datalake restituendo colonne e righe. Supporta la proiezione di un sottoinsieme " +
        "di colonne per sfruttare la natura columnar del formato e ridurre i dati trasferiti/elaborati. " +
        "Se il risultato ha 'truncated' true, richiama nuovamente il tool passando 'offset' = 'nextOffset' " +
        "per leggere la pagina successiva.")]
    public async Task<ParquetReadResult> ReadParquetAsync(
        [Description("Percorso completo del file Parquet all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe da restituire (default 200, massimo 10000).")]
        int maxRows = DefaultMaxRows,
        [Description("Sottoinsieme di colonne da leggere. Se omesso vengono lette tutte le colonne.")]
        string[]? columns = null,
        [Description("Numero di righe da saltare prima di iniziare a restituire risultati, per leggere pagine successive di un file di grandi dimensioni.")]
        long offset = 0,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await parquetReader.ReadAsync(stream, boundedMaxRows, columns, boundedOffset, cancellationToken);
    }

    [McpServerTool(Name = "read_text"), Description(
        "Legge un file di testo (.txt) dal datalake restituendo le righe. La lettura si interrompe non appena " +
        "'maxLines' viene raggiunto, per non caricare file di grandi dimensioni in memoria inutilmente. " +
        "Se il risultato ha 'truncated' true, richiama nuovamente il tool passando 'offset' = 'nextOffset' " +
        "per leggere la pagina successiva.")]
    public async Task<TextReadResult> ReadTextAsync(
        [Description("Percorso completo del file di testo all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe da restituire (default 200, massimo 10000).")]
        int maxLines = DefaultMaxRows,
        [Description("Numero di righe da saltare prima di iniziare a restituire risultati, per leggere pagine successive di un file di grandi dimensioni.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxLines = Math.Clamp(maxLines, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await textReader.ReadAsync(stream, boundedMaxLines, boundedOffset, cancellationToken);
    }

    [McpServerTool(Name = "read_markdown"), Description(
        "Legge un file markdown (.md) dal datalake restituendo le righe. La lettura si interrompe non appena " +
        "'maxLines' viene raggiunto, per non caricare file di grandi dimensioni in memoria inutilmente. " +
        "Se il risultato ha 'truncated' true, richiama nuovamente il tool passando 'offset' = 'nextOffset' " +
        "per leggere la pagina successiva.")]
    public async Task<TextReadResult> ReadMarkdownAsync(
        [Description("Percorso completo del file markdown all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe da restituire (default 200, massimo 10000).")]
        int maxLines = DefaultMaxRows,
        [Description("Numero di righe da saltare prima di iniziare a restituire risultati, per leggere pagine successive di un file di grandi dimensioni.")]
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxLines = Math.Clamp(maxLines, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await textReader.ReadAsync(stream, boundedMaxLines, boundedOffset, cancellationToken);
    }
}
