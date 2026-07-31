using Datalake.Mcp.Server.Configuration;
using Datalake.Mcp.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// I log vanno su stderr: stdout e' riservato al protocollo MCP (stdio transport).
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddOptions<DataLakeOptions>()
    .Bind(builder.Configuration.GetSection(DataLakeOptions.SectionName));

builder.Services.AddSingleton<DataLakeService>();
builder.Services.AddSingleton<CsvReaderService>();
builder.Services.AddSingleton<ParquetReaderService>();
builder.Services.AddSingleton<TextReaderService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
