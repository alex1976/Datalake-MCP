using Datalake.Mcp.Server.Services;
using Parquet.Serialization;

namespace Datalake.Mcp.Server.Tests;

public class ParquetReaderServiceTests
{
    private readonly ParquetReaderService _sut = new();

    private sealed record SampleRow(int Id, string Name);

    [Fact]
    public async Task GetSchemaAsync_ReturnsColumnNames()
    {
        using var stream = await CreateSampleParquetAsync();

        var schema = await _sut.GetSchemaAsync(stream, CancellationToken.None);

        Assert.Equal(["Id", "Name"], schema.Select(c => c.Name));
    }

    [Fact]
    public async Task ReadAsync_ReturnsAllRowsWhenUnderMaxRows()
    {
        using var stream = await CreateSampleParquetAsync();

        var result = await _sut.ReadAsync(stream, maxRows: 100, columns: null, offset: 0, CancellationToken.None);

        Assert.Equal(3, result.Rows.Count);
        Assert.False(result.Truncated);
        Assert.Equal(3, result.TotalRowCount);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_ProjectsRequestedColumnsOnly()
    {
        using var stream = await CreateSampleParquetAsync();

        var result = await _sut.ReadAsync(stream, maxRows: 100, columns: ["Name"], offset: 0, CancellationToken.None);

        Assert.Equal(["Name"], result.Columns);
        Assert.All(result.Rows, row => Assert.False(row.ContainsKey("Id")));
    }

    [Fact]
    public async Task ReadAsync_StopsAtMaxRowsAndReportsTruncation()
    {
        using var stream = await CreateSampleParquetAsync();

        var result = await _sut.ReadAsync(stream, maxRows: 2, columns: null, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal(3, result.TotalRowCount);
        Assert.Equal(2, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffset_SkipsAlreadyReturnedRows()
    {
        using var stream = await CreateSampleParquetAsync();

        var result = await _sut.ReadAsync(stream, maxRows: 2, columns: null, offset: 2, CancellationToken.None);

        Assert.Equal(2, result.Offset);
        Assert.Single(result.Rows);
        Assert.Equal("Carol", result.Rows[0]["Name"]);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffsetPastEnd_ReturnsNoRows()
    {
        using var stream = await CreateSampleParquetAsync();

        var result = await _sut.ReadAsync(stream, maxRows: 100, columns: null, offset: 10, CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.False(result.Truncated);
        Assert.Equal(3, result.TotalRowCount);
        Assert.Null(result.NextOffset);
    }

    private static async Task<MemoryStream> CreateSampleParquetAsync()
    {
        var rows = new[]
        {
            new SampleRow(1, "Alice"),
            new SampleRow(2, "Bob"),
            new SampleRow(3, "Carol"),
        };

        var stream = new MemoryStream();
        await ParquetSerializer.SerializeAsync(rows, stream);
        stream.Position = 0;
        return stream;
    }
}
