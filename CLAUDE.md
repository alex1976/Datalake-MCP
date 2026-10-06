# PURPOSE
the purpose of this project is to develop an MCP server to expose an azure datalake to Claude.

# SPECIFICATION
The MCP server must be able to navigate the files contained in the azure gen2 type datalake and read the contents in csv or parquet format.

The user must be able to request data, specify filters, and express queries against the data contained in the data lake.

The MCP server must also be able to save files (text, markdown, CSV, Parquet, PDF) in a folder or subfolder of the data lake with the tools save_text, save_markdown, save_csv, save_parquet and save_pdf. Name and path of every saved file are recorded in an index file (a CSV at the filesystem root, configured by DataLake:SavedFilesIndexPath). The search_file tool (parameters nomeFile, tipoFile) searches that index with "contains" logic on the file name; when exactly one file matches, its content is returned too. Existing files are never overwritten unless overwrite is true.

# TECHNICAL NOTES
Good reading performance and data extraction efficiency are required.

Writes must be conditional (no silent overwrite) and the index must be updated with optimistic concurrency (ETag).

The datalake will be identified by an accountName and an AccountKey specified in configuration parameters.