using System.Text;
using Datalake.Mcp.Server.Services;

namespace Datalake.Mcp.Server.Tests;

public class CsvReaderServiceTests
{
    private readonly CsvReaderService _sut = new();

    [Fact]
    public async Task ReadAsync_ParsesHeaderAndRows()
    {
        using var stream = ToStream("id,name\n1,Alice\n2,Bob\n");

        var result = await _sut.ReadAsync(stream, maxRows: 100, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None);

        Assert.Equal(["id", "name"], result.Columns);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Alice", result.Rows[0]["name"]);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_StopsAtMaxRowsAndReportsTruncation()
    {
        using var stream = ToStream("id\n1\n2\n3\n4\n5\n");

        var result = await _sut.ReadAsync(stream, maxRows: 2, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_ExactlyMaxRows_DoesNotReportTruncation()
    {
        using var stream = ToStream("id\n1\n2\n");

        var result = await _sut.ReadAsync(stream, maxRows: 2, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithoutHeader_GeneratesColumnNames()
    {
        using var stream = ToStream("1,Alice\n2,Bob\n");

        var result = await _sut.ReadAsync(stream, maxRows: 100, delimiter: ",", hasHeader: false, offset: 0, CancellationToken.None);

        Assert.Equal(["column_0", "column_1"], result.Columns);
        Assert.Equal("Alice", result.Rows[0]["column_1"]);
    }

    [Fact]
    public async Task ReadAsync_WithOffset_SkipsAlreadyReturnedRows()
    {
        using var stream = ToStream("id\n1\n2\n3\n4\n5\n");

        var result = await _sut.ReadAsync(stream, maxRows: 2, delimiter: ",", hasHeader: true, offset: 2, CancellationToken.None);

        Assert.Equal(2, result.Offset);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("3", result.Rows[0]["id"]);
        Assert.Equal("4", result.Rows[1]["id"]);
        Assert.True(result.Truncated);
        Assert.Equal(4, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffsetPastEnd_ReturnsNoRows()
    {
        using var stream = ToStream("id\n1\n2\n");

        var result = await _sut.ReadAsync(stream, maxRows: 100, delimiter: ",", hasHeader: true, offset: 10, CancellationToken.None);

        Assert.Empty(result.Rows);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
