# PURPOSE
the purpose of this project is to develop an MCP server to expose an azure datalake to Claude.

# SPECIFICATION
The MCP server must be able to navigate the files contained in the azure gen2 type datalake and read the contents in csv or parquet format.

The user must be able to request data, specify filters, and express queries against the data contained in the data lake.

# TECHNICAL NOTES
Good reading performance and data extraction efficiency are required.

The datalake will be identified by an accountName and an AccountKey specified in configuration parameters.