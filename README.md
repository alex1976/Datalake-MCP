# Datalake-MCP

**Server MCP** (Model Context Protocol) che espone un Azure Data Lake Storage Gen2 a Claude,
consentendo di navigare i file e leggerne il contenuto in formato CSV, Parquet, testo (.txt),
markdown (.md) o XML.

## Struttura della soluzione

```
Datalake-MCP.sln
src/Datalake.Mcp.Server/         Server MCP (host .NET Generic Host, trasporto stdio)
  Configuration/DataLakeOptions.cs   Opzioni di connessione (AccountName/AccountKey/DefaultFileSystem)
  Services/DataLakeService.cs        Wrapper su Azure.Storage.Files.DataLake (navigazione, streaming)
  Services/CsvReaderService.cs       Lettura CSV con limite di righe (streaming)
  Services/ParquetReaderService.cs   Lettura Parquet a row-group, con proiezione colonne
  Services/TextReaderService.cs      Lettura file di testo/markdown riga per riga (streaming)
  Services/XmlReaderService.cs       Lettura XML tabellare elemento per elemento, con proiezione colonne
  Tools/DataLakeTools.cs             Tool MCP esposti al client (list_filesystems, read_csv, ...)
tests/Datalake.Mcp.Server.Tests/ Test xUnit per i reader CSV/Parquet/testo/XML
```

## Tool MCP esposti

| Tool | Descrizione |
|---|---|
| `list_filesystems` | Elenca i filesystem (container) dell'account |
| `list_directory` | Elenca file e directory in un percorso (opzionalmente ricorsivo) |
| `get_file_info` | Metadati di un file (dimensione, data ultima modifica) |
| `read_csv` | Legge un CSV, con limite `maxRows`, delimitatore configurabile e `offset` per la paginazione |
| `get_parquet_schema` | Restituisce lo schema (colonne/tipi) di un Parquet senza leggerne i dati |
| `read_parquet` | Legge un Parquet, con `maxRows`, proiezione opzionale delle colonne e `offset` per la paginazione |
| `read_text` | Legge un file di testo (.txt), con `maxLines` e `offset` per la paginazione |
| `read_markdown` | Legge un file markdown (.md), con `maxLines` e `offset` per la paginazione |
| `get_xml_schema` | Restituisce lo schema (colonne, tra attributi ed elementi) dell'elemento record di un XML tabellare senza leggerne i dati |
| `read_xml` | Legge un XML tabellare (radice con elementi record ripetuti), con `maxRows`, elemento record e proiezione colonne opzionali, e `offset` per la paginazione |

Le letture sono progettate per non caricare interi file in memoria: CSV, testo/markdown e XML
vengono letti riga/elemento per riga/elemento fermandosi al limite richiesto, mentre Parquet viene
letto un row-group alla volta. I file remoti vengono aperti con streaming a caricamento differito
(`DataLakeFileClient.OpenReadAsync`), che scarica solo i byte effettivamente necessari. La lettura
XML disabilita il DTD processing e la risoluzione di entità esterne per evitare attacchi XXE.

### Paginazione

Le risposte di `read_csv`, `read_parquet`, `read_text`, `read_markdown` e `read_xml` sono soggette
al limite di dimensione dei tool call (circa 1 MB). Per file che eccedono `maxRows`/`maxLines` (o
il limite di dimensione), il risultato riporta
`truncated: true` e un `nextOffset`: richiamando nuovamente il tool con `offset = nextOffset` si
ottiene la pagina successiva, saltando le righe già restituite senza doverle rileggere per intero.
Quando non c'è altro da leggere, `truncated` è `false` e `nextOffset` è `null`.

## Configurazione

Le credenziali dell'account (`accountName` e `accountKey`) non vanno mai committate. In sviluppo,
usa i .NET user-secrets dalla cartella `src/Datalake.Mcp.Server`:

```powershell
dotnet user-secrets init
dotnet user-secrets set "DataLake:AccountName" "<nome-account>"
dotnet user-secrets set "DataLake:AccountKey" "<chiave-account>"
dotnet user-secrets set "DataLake:DefaultFileSystem" "<container-predefinito>"
```

In alternativa, tramite variabili d'ambiente (utile per il deploy):

```powershell
$env:DataLake__AccountName = "<nome-account>"
$env:DataLake__AccountKey = "<chiave-account>"
$env:DataLake__DefaultFileSystem = "<container-predefinito>"
```

`appsettings.json` contiene solo i placeholder vuoti e può restare nel repository.

## Build, test ed esecuzione

```powershell
dotnet build
dotnet test
dotnet run --project src/Datalake.Mcp.Server
```

Il server comunica via stdio secondo il protocollo MCP: va quindi registrato come server MCP nel
client (es. Claude Desktop / Claude Code), puntando all'eseguibile del progetto
`Datalake.Mcp.Server`, non lanciato manualmente in un terminale interattivo.

## Registrazione in Claude Desktop e utilizzo dell'MCP

Un esempio di configurazione è disponibile in
[`claude_desktop_config.example.json`](claude_desktop_config.example.json). Per usarlo:

1. Esegui `dotnet build src/Datalake.Mcp.Server -c Release` per generare il DLL in `bin/Release/net9.0`. Oppure usa il contenuto della cartella se già compilato.
2. Copia il contenuto del file di esempio nel file di configurazione di Claude Desktop
   (`%APPDATA%\Claude\claude_desktop_config.json`), unendolo alla chiave `mcpServers` se già presente.
3. Aggiorna il percorso in `args` se la repository si trova altrove, e valorizza `AccountName`/`AccountKey`/`DefaultFileSystem` in `env` con le credenziali reali (non committarle mai nel repository).
4. Riavvia Claude Desktop: il server "datalake" comparirà tra i tool disponibili.
