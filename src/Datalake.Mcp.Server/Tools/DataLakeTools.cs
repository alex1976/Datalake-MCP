using System.ComponentModel;
using System.Text.Json;
using Datalake.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Datalake.Mcp.Server.Tools;

[McpServerToolType]
public sealed class DataLakeTools(
    DataLakeService dataLake,
    CsvReaderService csvReader,
    ParquetReaderService parquetReader,
    TextReaderService textReader,
    XmlReaderService xmlReader,
    FileWriterService fileWriter)
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
        [Description("Filtri opzionali (colonna/operatore/valore) combinati in AND; vengono applicati prima di offset e maxRows, che contano quindi solo le righe corrispondenti. Esempio: [{\"column\":\"stato\",\"operator\":\"eq\",\"value\":\"APERTO\"},{\"column\":\"importo\",\"operator\":\"gt\",\"value\":\"1000\"}].")]
        FilterCondition[]? filter = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await csvReader.ReadAsync(stream, boundedMaxRows, delimiter, hasHeader, boundedOffset, cancellationToken, filter);
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
        [Description("Filtri opzionali (colonna/operatore/valore) combinati in AND; vengono applicati prima di offset e maxRows, che contano quindi solo le righe corrispondenti. Esempio: [{\"column\":\"stato\",\"operator\":\"eq\",\"value\":\"APERTO\"},{\"column\":\"importo\",\"operator\":\"gt\",\"value\":\"1000\"}].")]
        FilterCondition[]? filter = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await parquetReader.ReadAsync(stream, boundedMaxRows, columns, boundedOffset, cancellationToken, filter);
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
        [Description("Se specificato, vengono considerate solo le righe che contengono questo testo (case-insensitive); offset e maxLines contano solo tali righe.")]
        string? contains = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxLines = Math.Clamp(maxLines, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await textReader.ReadAsync(stream, boundedMaxLines, boundedOffset, cancellationToken, contains);
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
        [Description("Se specificato, vengono considerate solo le righe che contengono questo testo (case-insensitive); offset e maxLines contano solo tali righe.")]
        string? contains = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxLines = Math.Clamp(maxLines, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await textReader.ReadAsync(stream, boundedMaxLines, boundedOffset, cancellationToken, contains);
    }

    [McpServerTool(Name = "get_xml_schema"), Description(
        "Restituisce lo schema (nomi colonna, tra attributi ed elementi figli) di un file XML tabellare, individuando " +
        "l'elemento record senza leggerne tutti i dati, utile per decidere quali colonne proiettare con read_xml.")]
    public async Task<IReadOnlyList<XmlColumnInfo>> GetXmlSchemaAsync(
        [Description("Percorso completo del file XML all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Nome locale dell'elemento XML che rappresenta un record/riga (es. 'Product'). Se omesso viene usato il primo elemento figlio della radice.")]
        string? recordElement = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await xmlReader.GetSchemaAsync(stream, recordElement, cancellationToken);
    }

    [McpServerTool(Name = "read_xml"), Description(
        "Legge un file XML tabellare dal datalake (una radice contenente elementi record ripetuti, es. " +
        "<Root><Record>...</Record>...</Root>) restituendo colonne e righe. L'elemento record viene individuato " +
        "automaticamente (primo figlio della radice) salvo specificarlo con 'recordElement'. La lettura si interrompe " +
        "non appena 'maxRows' viene raggiunto. Se il risultato ha 'truncated' true, richiama nuovamente il tool " +
        "passando 'offset' = 'nextOffset' per leggere la pagina successiva.")]
    public async Task<XmlReadResult> ReadXmlAsync(
        [Description("Percorso completo del file XML all'interno del filesystem.")] string path,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe (record) da restituire (default 200, massimo 10000).")]
        int maxRows = DefaultMaxRows,
        [Description("Nome locale dell'elemento XML che rappresenta un record/riga (es. 'Product'). Se omesso viene usato il primo elemento figlio della radice.")]
        string? recordElement = null,
        [Description("Sottoinsieme di colonne (attributi o elementi figli del record) da leggere. Se omesso vengono lette tutte le colonne trovate nel primo record.")]
        string[]? columns = null,
        [Description("Numero di record da saltare prima di iniziare a restituire risultati, per leggere pagine successive di un file di grandi dimensioni.")]
        int offset = 0,
        [Description("Filtri opzionali (colonna/operatore/valore) combinati in AND; vengono applicati prima di offset e maxRows, che contano quindi solo le righe corrispondenti. Esempio: [{\"column\":\"stato\",\"operator\":\"eq\",\"value\":\"APERTO\"},{\"column\":\"importo\",\"operator\":\"gt\",\"value\":\"1000\"}].")]
        FilterCondition[]? filter = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);
        var boundedOffset = Math.Max(0, offset);

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, path, cancellationToken);
        return await xmlReader.ReadAsync(stream, boundedMaxRows, recordElement, columns, boundedOffset, cancellationToken, filter);
    }

    private const string SaveNote =
        " Il file viene registrato (percorso, nome, formato, dimensione, data, descrizione) nel file di indice " +
        "del filesystem (DataLake:SavedFilesIndexPath, un CSV leggibile con read_csv). Le directory mancanti vengono create. " +
        "Un file già esistente non viene sostituito a meno che overwrite sia true.";

    private async Task<FileIndexEntry> SaveAsync(
        string path, string[] extensions, string format, byte[] content, string? fileSystem, string? description,
        bool overwrite, CancellationToken cancellationToken)
    {
        if (!extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Il percorso deve terminare con {string.Join(" o ", extensions)}.", nameof(path));
        }

        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        return await dataLake.SaveFileAsync(resolvedFileSystem, path, content, format, description, overwrite, cancellationToken);
    }

    [McpServerTool(Name = "search_file"), Description(
        "Cerca nell'indice dei file salvati con i tool save_* (non scansiona il datalake). Restituisce i file il cui nome " +
        "contiene 'nomeFile' (case-insensitive) e, se indicato, di tipo 'tipoFile'; il 'path' restituito si passa " +
        "direttamente ai tool read_* o get_file_info. Se viene trovato un solo file ne restituisce anche il contenuto " +
        "(csv, parquet, txt, md; per i pdf solo i metadati): se il contenuto ha 'truncated' true, continua con il tool " +
        "read_* corrispondente passando 'offset' = 'nextOffset'. Con più file restituisce solo l'elenco.")]
    public async Task<SearchFileResult> SearchFileAsync(
        [Description("Testo che il nome del file deve contenere (anche parziale, case-insensitive). Se omesso non filtra per nome.")]
        string? nomeFile = null,
        [Description("Tipo di file: csv, parquet, pdf, md, txt (accettati anche '.csv', 'markdown', 'text'). Se omesso non filtra per tipo.")]
        string? tipoFile = null,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Numero massimo di righe del contenuto da restituire se viene trovato un solo file (default 200, massimo 10000).")]
        int maxRows = DefaultMaxRows,
        [Description("Separatore di campo usato per leggere il contenuto se l'unico file trovato è un CSV.")]
        string delimiter = ",",
        CancellationToken cancellationToken = default)
    {
        var resolvedFileSystem = dataLake.ResolveFileSystem(fileSystem);
        var files = await dataLake.SearchFilesAsync(resolvedFileSystem, nomeFile, tipoFile, cancellationToken);
        if (files.Count != 1)
        {
            return new SearchFileResult(files, null, null);
        }

        var file = files[0];
        var boundedMaxRows = Math.Clamp(maxRows, 1, HardMaxRows);

        if (file.Format is not ("csv" or "parquet" or "txt" or "md"))
        {
            return new SearchFileResult(files, null, $"Il contenuto dei file '{file.Format}' non è leggibile come testo: sono disponibili solo i metadati.");
        }

        await using var stream = await dataLake.OpenReadAsync(resolvedFileSystem, file.Path, cancellationToken);
        object content = file.Format switch
        {
            "csv" => await csvReader.ReadAsync(stream, boundedMaxRows, delimiter, true, 0, cancellationToken, null),
            "parquet" => await parquetReader.ReadAsync(stream, boundedMaxRows, null, 0, cancellationToken, null),
            _ => await textReader.ReadAsync(stream, boundedMaxRows, 0, cancellationToken, null),
        };

        return new SearchFileResult(files, content, null);
    }

    [McpServerTool(Name = "save_text"), Description("Salva un file di testo (.txt) nel datalake." + SaveNote)]
    public async Task<FileIndexEntry> SaveTextAsync(
        [Description("Percorso completo del file da creare (cartelle/sottocartelle incluse), es. 'report/2026/note.txt'.")] string path,
        [Description("Contenuto testuale del file.")] string content,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Descrizione opzionale da riportare nel file di indice.")] string? description = null,
        [Description("Se true sostituisce un file già esistente.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => await SaveAsync(path, [".txt"], "txt", fileWriter.BuildText(content), fileSystem, description, overwrite, cancellationToken);

    [McpServerTool(Name = "save_markdown"), Description("Salva un file markdown (.md) nel datalake." + SaveNote)]
    public async Task<FileIndexEntry> SaveMarkdownAsync(
        [Description("Percorso completo del file da creare (cartelle/sottocartelle incluse), es. 'report/2026/sintesi.md'.")] string path,
        [Description("Contenuto markdown del file.")] string content,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Descrizione opzionale da riportare nel file di indice.")] string? description = null,
        [Description("Se true sostituisce un file già esistente.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => await SaveAsync(path, [".md"], "md", fileWriter.BuildText(content), fileSystem, description, overwrite, cancellationToken);

    [McpServerTool(Name = "save_csv"), Description(
        "Salva un file CSV (.csv) nel datalake. Il contenuto deve essere testo CSV completo (intestazione inclusa) " +
        "con lo stesso numero di campi in ogni riga." + SaveNote)]
    public async Task<FileIndexEntry> SaveCsvAsync(
        [Description("Percorso completo del file da creare (cartelle/sottocartelle incluse), es. 'export/2026/clienti.csv'.")] string path,
        [Description("Contenuto CSV completo, intestazione inclusa.")] string content,
        [Description("Carattere separatore di campo.")] string delimiter = ",",
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Descrizione opzionale da riportare nel file di indice.")] string? description = null,
        [Description("Se true sostituisce un file già esistente.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => await SaveAsync(path, [".csv"], "csv", fileWriter.BuildCsv(content, delimiter), fileSystem, description, overwrite, cancellationToken);

    [McpServerTool(Name = "save_parquet"), Description(
        "Salva un file Parquet (.parquet) nel datalake a partire da un elenco di righe. Il tipo di ogni colonna viene " +
        "dedotto dai valori (bool, intero, decimale, altrimenti stringa); tutte le colonne ammettono null." + SaveNote)]
    public async Task<FileIndexEntry> SaveParquetAsync(
        [Description("Percorso completo del file da creare (cartelle/sottocartelle incluse), es. 'export/2026/clienti.parquet'.")] string path,
        [Description("Righe da scrivere: array di oggetti nome colonna → valore, es. [{\"id\":1,\"nome\":\"Rossi\"},{\"id\":2,\"nome\":null}].")]
        Dictionary<string, JsonElement>[] rows,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Descrizione opzionale da riportare nel file di indice.")] string? description = null,
        [Description("Se true sostituisce un file già esistente.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => await SaveAsync(path, [".parquet"], "parquet", await fileWriter.BuildParquetAsync(rows, cancellationToken),
            fileSystem, description, overwrite, cancellationToken);

    [McpServerTool(Name = "save_pdf"), Description(
        "Salva un file PDF (.pdf) nel datalake. Il contenuto va fornito come PDF già generato, codificato in base64." + SaveNote)]
    public async Task<FileIndexEntry> SavePdfAsync(
        [Description("Percorso completo del file da creare (cartelle/sottocartelle incluse), es. 'documenti/2026/offerta.pdf'.")] string path,
        [Description("Contenuto del PDF codificato in base64.")] string contentBase64,
        [Description("Nome del filesystem (container). Se omesso viene usato DataLake:DefaultFileSystem.")]
        string? fileSystem = null,
        [Description("Descrizione opzionale da riportare nel file di indice.")] string? description = null,
        [Description("Se true sostituisce un file già esistente.")] bool overwrite = false,
        CancellationToken cancellationToken = default)
        => await SaveAsync(path, [".pdf"], "pdf", fileWriter.BuildPdf(contentBase64), fileSystem, description, overwrite, cancellationToken);
}
