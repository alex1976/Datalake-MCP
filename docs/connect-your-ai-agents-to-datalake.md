# Connect your AI agents to DATALAKE to read and store data

*How Datalake-MCP turns an Azure Data Lake Storage Gen2 account into a set of tools that Claude can navigate, query and write to.*

---

Most of the data a company cares about does not live in a database an LLM can talk to. It lives in a data lake: thousands of CSV and Parquet files, organised in folders by domain, year or source system. Getting an AI agent to use that data usually means writing a custom pipeline, exporting samples by hand, or pasting rows into a chat window.

The Model Context Protocol (MCP) gives us a cleaner option. If the data lake is exposed as an MCP server, any MCP-capable client (Claude Desktop, Claude Code, your own agent) can discover and use it as a set of typed tools.

That is what **Datalake-MCP** does. It is a .NET 9 MCP server that connects to an Azure Data Lake Storage Gen2 account and lets an agent:

- **navigate** filesystems and directories,
- **read** CSV, Parquet, XML, text and markdown files, with filters, column projection and pagination,
- **save** text, markdown, CSV, Parquet and PDF files, with a searchable index of everything that has been written.

This post covers how it works internally and what it makes possible.

## Architecture at a glance

```
User <-> Claude (Desktop / Code) <-- MCP over stdio --> Datalake.Mcp.Server (.NET 9)
                                                          |-- Tools/DataLakeTools
                                                          |-- Readers: CSV, Parquet, Text, XML, RowFilter
                                                          |-- Writers: FileWriterService, FileIndex
                                                          '-- DataLakeService
                                                                  |
                                                      Azure.Storage.Files.DataLake
                                                      (AccountName + AccountKey)
                                                                  |
                                                      Azure Data Lake Storage Gen2
```

The server runs as a .NET Generic Host and talks to the client over **stdio**. The client launches it as a child process, so there is no HTTP endpoint to secure and nothing to deploy. Connection settings (`AccountName`, `AccountKey`, `DefaultFileSystem`) come from .NET user-secrets in development, or from environment variables when the server is registered in the client.

The code is split into three layers:

- **Tools** (`DataLakeTools`): the MCP surface. Each method is a tool with a description and typed parameters that the model sees.
- **Services**: format-specific readers, the writer, the index and the row filter. They contain no Azure-specific code.
- **DataLakeService**: a thin wrapper over `Azure.Storage.Files.DataLake`, the only class that talks to the storage account.

## The tool surface

| Area | Tools |
|---|---|
| Navigation | `list_filesystems`, `list_directory`, `get_file_info` |
| Tabular reads | `read_csv`, `read_parquet`, `read_xml`, plus `get_parquet_schema` and `get_xml_schema` |
| Text reads | `read_text`, `read_markdown` |
| Writes | `save_text`, `save_markdown`, `save_csv`, `save_parquet`, `save_pdf` |
| Retrieval | `search_file` |

The tools are designed to be called in sequence. A typical exploration goes `list_filesystems`, then `list_directory`, then `get_parquet_schema`, then `read_parquet`. The agent sees column names and types before it asks for rows, so it can write sensible filters instead of guessing.

## Reading without loading: streaming by design

A data lake file can be gigabytes in size, and an LLM can only consume a tiny fraction of it. The server is built around that mismatch.

**Lazy remote reads.** Files are opened with `DataLakeFileClient.OpenReadAsync`, which returns a stream that downloads only the bytes actually requested. Nothing is fetched up front.

**Format-aware streaming.**

- CSV, text and markdown are read line by line and the read stops at the requested limit.
- XML is read element by element (tabular XML: a root with repeated record elements). DTD processing and external entity resolution are disabled, which prevents XXE attacks.
- Parquet is read one **row group** at a time, with **column projection**: if you ask for three columns, the others are never decoded. This is where Parquet's columnar layout pays off.

**Pagination that does not re-read.** Every read tool returns `truncated` and `nextOffset`. When a result is cut off, either by `maxRows` or by the tool-call size limit (about 1 MB), the agent calls the tool again with `offset = nextOffset` and gets the next page. The model walks a large file in chunks without any hidden state in the server.

## Querying: server-side filters

The most useful feature for analytical work is the `filter` parameter on `read_csv`, `read_parquet` and `read_xml`. It is a list of conditions combined with AND:

```json
[
  {"column": "stato",   "operator": "eq", "value": "APERTO"},
  {"column": "importo", "operator": "gt", "value": "1000"}
]
```

Supported operators are `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `contains`, `startswith`, `endswith`, `isnull` and `isnotnull`. Comparison semantics:

- numeric when both the cell and the value parse as numbers (invariant format, `12.5`),
- timestamp-aware for Parquet date columns when the value is a date,
- case-insensitive text otherwise.

Two design decisions matter here:

1. **The filter runs inside the streaming loop, before `offset` and `maxRows`.** The pagination counters therefore count only matching rows. "Give me the next 100 open orders over 1000" works as expected, and non-matching rows never reach the model's context window.
2. **Filters can reference columns that are not projected.** You can filter on `stato` and return only `id` and `importo`.

A wrong column name or operator returns an error that lists the allowed values, so the agent can correct itself in the next call without human help.

A request like *"show me the open orders above 1000"* becomes: look at the schema, build a filter, read with a projection, and summarise the result. The model never sees the other 99% of the file.

## Writing back: safe by default

Reading is only half of the story. Agents are far more useful when they can leave something behind: a report, a cleaned dataset, a summary of an analysis. Write access to shared storage is also where things go wrong, so the write path was designed conservatively.

The `save_*` tools take a `path` (folders and subfolders are created if missing), a `fileSystem`, a `description` and an `overwrite` flag.

**No silent overwrite.** Uploads are conditional (`If-None-Match: *`). If the file already exists and `overwrite` is not `true`, the call fails and the existing file is untouched. An agent cannot destroy data by accident, only by being explicitly told to.

**Validation before upload.**

- The path extension must match the format.
- CSV must have the same number of fields on every row.
- Parquet is built from a list of rows (column to value objects), with types inferred (bool, integer, decimal, string).
- PDF is received as base64, and the `%PDF-` header is checked.

## The index: files you can find again

Each successful save is recorded in an **index file**, a CSV at the filesystem root (configured with `DataLake:SavedFilesIndexPath`, default `saved-files-index.csv`) with the columns:

```
path, name, format, sizeBytes, savedAt, description
```

Saving the same path again updates its row. Because several agents or sessions may write at the same time, the index is updated with **optimistic concurrency**: read the index and its ETag, apply the change, write back with `If-Match: <ETag>`. On a conflict the cycle restarts, up to 5 attempts. No locks, no lease management, and no lost updates.

The index is a plain CSV, so it can be read with `read_csv`, opened in Excel or loaded by any other tool.

On top of the index sits `search_file(nomeFile, tipoFile)`. It matches the file name with *contains* logic (case-insensitive) and can filter by type (csv, parquet, pdf, md, txt). When **exactly one file matches**, the tool also returns its content: the first `maxRows` rows for tabular files, text for txt and md, metadata only for PDF. The agent can ask for "last quarter's margin report" and get the document in a single call.

## What this enables

Once the connection is in place, several patterns become possible.

**1. Natural-language analytics over raw files.** Ask questions about costs, orders or KPIs without building a dashboard first. The agent inspects the schema, filters, aggregates what comes back and explains it. It works well for ad-hoc questions that do not justify a BI project.

**2. Data exploration and profiling.** Point the agent at an unfamiliar folder. It can list the files, read the schemas, sample rows, spot null-heavy columns and describe what the dataset seems to contain, which is a good first step when you inherit a lake nobody documented.

**3. Persistent agent output.** Reports, generated markdown documentation, cleaned CSVs and Parquet extracts can be saved next to the data, indexed and found again later. The lake becomes the shared memory of your agents, backed by your existing storage, access control and retention rules.

**4. Multi-agent workflows with a shared medium.** One agent extracts and filters data and saves a Parquet file. Another finds it with `search_file` and builds a summary. A third saves the PDF. The agents never talk directly. They coordinate through files and the index, and every step leaves an auditable artifact.

**5. Reference data lookups.** Price lists, catalogues and code tables stored as CSV with a markdown index file can be searched by keyword and compared across years or regions, with the agent doing the matching that used to be done by hand.

**6. Domain skills on top of the tools.** The repository includes a `SKILL.md` that teaches Claude how a specific lake is organised: folder conventions, table meanings, business terms (EVM, WBS, SAL and so on). The MCP provides the capabilities and the skill provides the domain knowledge. Together they let a general-purpose model act like a specialist for your data.

## Getting started

1. Build the server: `dotnet build src/Datalake.Mcp.Server -c Release`
2. Add it to `%APPDATA%\Claude\claude_desktop_config.json` under `mcpServers`, with the DLL path in `args` and `DataLake__AccountName`, `DataLake__AccountKey` and `DataLake__DefaultFileSystem` in `env`.
3. Restart Claude Desktop. The `datalake` server and its tools appear in the tool list.

Then ask something like: *"List the filesystems, find the Parquet files under `sales/2026`, and show me the open orders above 1000 with only id, customer and amount."*

## Security notes

- Never commit credentials. Use user-secrets or environment variables; `appsettings.json` ships with empty placeholders.
- The server authenticates with the **account key**, which grants full access to the account. For production, consider a dedicated storage account or a narrower credential, and be deliberate about which lake you connect to an agent.
- Writes are additive by default. `overwrite=true` is the only way to replace a file.
- Reads are bounded (`maxRows`, `maxLines`, the response size cap), so a careless prompt cannot stream a whole file into the model's context.

## Conclusion

An agent is only as useful as the data it can reach. Exposing Azure Data Lake Gen2 over MCP removes the glue code between the two: the agent can browse, filter, read and write through a small set of typed tools, while streaming reads, conditional writes and ETag-based indexing keep the server efficient and safe.

Datalake-MCP is open source and listed on [mcpservers.org](https://mcpservers.org/servers/alex1976/datalake-mcp). If you have a lake and an agent, try connecting them.

---

*Tags: MCP, Azure Data Lake, AI Agents, Claude, .NET, Data Engineering*
