namespace Datalake.Mcp.Server.Configuration;

public sealed class DataLakeOptions
{
    public const string SectionName = "DataLake";

    public string AccountName { get; set; } = string.Empty;

    public string AccountKey { get; set; } = string.Empty;

    /// <summary>Nome del filesystem (container) predefinito, se non specificato esplicitamente nelle richieste.</summary>
    public string? DefaultFileSystem { get; set; }
}
