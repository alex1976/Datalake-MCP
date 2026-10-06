# Datalake-MCP

**Server MCP** (Model Context Protocol) che espone un Azure Data Lake Storage Gen2 a Claude,
consentendo di navigare i file, leggerne il contenuto in formato CSV, Parquet, testo (.txt),
markdown (.md) o XML, e di salvare file (testo, markdown, CSV, Parquet, PDF) con un indice ricercabile.

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
  Services/FileWriterService.cs     Costruzione/validazione dei byte da salvare (testo, CSV, Parquet, PDF)
  Services/FileIndex.cs              Indice CSV dei file salvati (upsert, parsing, ricerca)
  Tools/DataLakeTools.cs             Tool MCP esposti al client (list_filesystems, read_csv, ...)
tests/Datalake.Mcp.Server.Tests/ Test xUnit per reader CSV/Parquet/testo/XML, writer e indice
```

## Tool MCP esposti

| Tool | Descrizione |
|---|---|
| `list_filesystems` | Elenca i filesystem (container) dell'account |
| `list_directory` | Elenca file e directory in un percorso (opzionalmente ricorsivo) |
| `get_file_info` | Metadati di un file (dimensione, data ultima modifica) |
| `read_csv` | Legge un CSV, con limite `maxRows`, delimitatore configurabile e `offset` per la paginazione; filtro opzionale `filter` |
| `get_parquet_schema` | Restituisce lo schema (colonne/tipi) di un Parquet senza leggerne i dati |
| `read_parquet` | Legge un Parquet, con `maxRows`, proiezione opzionale delle colonne e `offset` per la paginazione; filtro opzionale `filter` |
| `read_text` | Legge un file di testo (.txt), con `maxLines` e `offset` per la paginazione; filtro opzionale `contains` |
| `read_markdown` | Legge un file markdown (.md), con `maxLines` e `offset` per la paginazione; filtro opzionale `contains` |
| `get_xml_schema` | Restituisce lo schema (colonne, tra attributi ed elementi) dell'elemento record di un XML tabellare senza leggerne i dati |
| `read_xml` | Legge un XML tabellare (radice con elementi record ripetuti), con `maxRows`, elemento record e proiezione colonne opzionali, e `offset` per la paginazione; filtro opzionale `filter` |

| `save_text` | Salva un file `.txt` in una cartella/sottocartella del datalake |
| `save_markdown` | Salva un file `.md` |
| `save_csv` | Salva un file `.csv` da testo CSV (validato: stesso numero di campi per riga) |
| `save_parquet` | Salva un file `.parquet` da un elenco di righe (oggetti colonna → valore); i tipi sono dedotti (bool, intero, decimale, stringa) |
| `save_pdf` | Salva un file `.pdf` da contenuto base64 (verificata l'intestazione `%PDF-`) |
| `search_file` | Cerca nell'indice dei file salvati per `nomeFile` (contiene, case-insensitive) e/o `tipoFile` (csv, parquet, pdf, md, txt) e restituisce le voci trovate (`path` utilizzabile con i tool `read_*`). Se il risultato è un solo file, ne restituisce anche il contenuto (csv, parquet, txt, md; primi `maxRows` righe, poi si prosegue con `read_*` e `nextOffset`; per i pdf solo i metadati) |

Le letture sono progettate per non caricare interi file in memoria: CSV, testo/markdown e XML
vengono letti riga/elemento per riga/elemento fermandosi al limite richiesto, mentre Parquet viene
letto un row-group alla volta. I file remoti vengono aperti con streaming a caricamento differito
(`DataLakeFileClient.OpenReadAsync`), che scarica solo i byte effettivamente necessari. La lettura
XML disabilita il DTD processing e la risoluzione di entità esterne per evitare attacchi XXE.

### Filtri

`read_csv`, `read_parquet` e `read_xml` accettano un parametro opzionale `filter`: un elenco di
condizioni `{ column, operator, value }` combinate in AND, ad esempio:

```json
[{"column": "stato", "operator": "eq", "value": "APERTO"},
 {"column": "importo", "operator": "gt", "value": "1000"}]
```

Operatori: `eq` (`=`), `ne` (`!=`), `gt` (`>`), `gte` (`>=`), `lt` (`<`), `lte` (`<=`), `contains`,
`startswith`, `endswith`, `isnull`, `isnotnull` (questi ultimi due non richiedono `value`; una stringa
vuota conta come null). I confronti sono numerici quando cella e valore sono numeri (formato
invariante, es. `12.5`), sui timestamp Parquet quando il valore è una data, altrimenti testuali
case-insensitive. Il filtro può riferirsi anche a colonne non proiettate; una colonna inesistente o un
operatore non valido producono un errore che elenca i valori ammessi.

Il filtro viene applicato durante la lettura in streaming, **prima** di `offset` e `maxRows`: la
paginazione (`truncated`/`nextOffset`) conta quindi solo le righe che soddisfano il filtro.
`read_text` e `read_markdown` accettano invece `contains`, che seleziona le righe contenenti il testo
indicato (case-insensitive).

### Paginazione

Le risposte di `read_csv`, `read_parquet`, `read_text`, `read_markdown` e `read_xml` sono soggette
al limite di dimensione dei tool call (circa 1 MB). Per file che eccedono `maxRows`/`maxLines` (o
il limite di dimensione), il risultato riporta
`truncated: true` e un `nextOffset`: richiamando nuovamente il tool con `offset = nextOffset` si
ottiene la pagina successiva, saltando le righe già restituite senza doverle rileggere per intero.
Quando non c'è altro da leggere, `truncated` è `false` e `nextOffset` è `null`.

### Salvataggio file e indice

I tool `save_*` accettano `path` (cartelle e sottocartelle vengono create se mancanti), `fileSystem`,
`description` e `overwrite`. Senza `overwrite=true` un file esistente non viene mai sostituito (upload
condizionale `If-None-Match: *`). L'estensione del percorso deve corrispondere al formato.

Ogni salvataggio registra il file nell'**indice** `DataLake:SavedFilesIndexPath` (default
`saved-files-index.csv`, nella radice del filesystem), un CSV leggibile con `read_csv` con colonne
`path,name,format,sizeBytes,savedAt,description`. Salvare di nuovo lo stesso percorso aggiorna la riga
esistente. L'indice è aggiornato con concurrency ottimistica (ETag, fino a 5 tentativi).

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
