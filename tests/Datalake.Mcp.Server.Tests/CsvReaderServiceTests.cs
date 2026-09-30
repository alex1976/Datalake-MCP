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

    [Fact]
    public async Task ReadAsync_Filter_AppliesAndCombinedConditions()
    {
        using var stream = ToStream("id,name,amount\n1,Alice,50\n2,Bob,150\n3,Carol,250\n4,Dave,300\n");

        var result = await _sut.ReadAsync(stream, maxRows: 100, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None,
            [new FilterCondition("amount", "gt", "100"), new FilterCondition("name", "ne", "dave")]);

        Assert.Equal(["Bob", "Carol"], result.Rows.Select(r => r["name"]));
    }

    [Fact]
    public async Task ReadAsync_Filter_OffsetAndTruncationCountMatchingRowsOnly()
    {
        using var stream = ToStream("id,kind\n1,a\n2,b\n3,a\n4,b\n5,a\n6,a\n");

        var result = await _sut.ReadAsync(stream, maxRows: 2, delimiter: ",", hasHeader: true, offset: 1, CancellationToken.None,
            [new FilterCondition("kind", "eq", "a")]);

        Assert.Equal(["3", "5"], result.Rows.Select(r => r["id"]));
        Assert.True(result.Truncated);
        Assert.Equal(3, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_Filter_UnknownColumnThrowsListingAvailableColumns()
    {
        using var stream = ToStream("id,name\n1,Alice\n");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.ReadAsync(
            stream, maxRows: 10, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None,
            [new FilterCondition("missing", "eq", "x")]));

        Assert.Contains("id, name", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_Filter_InvalidOperatorThrows()
    {
        using var stream = ToStream("id\n1\n");

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ReadAsync(
            stream, maxRows: 10, delimiter: ",", hasHeader: true, offset: 0, CancellationToken.None,
            [new FilterCondition("id", "bogus", "1")]));
    }

    [Fact]
    public async Task ReadAsync_Filter_TextOperatorsAndNullChecks()
    {
        using var stream = ToStream("id,name\n1,Alice\n2,\n3,alberto\n");

        var starts = await _sut.ReadAsync(stream, 10, ",", true, 0, CancellationToken.None, [new FilterCondition("name", "startswith", "AL")]);
        Assert.Equal(["1", "3"], starts.Rows.Select(r => r["id"]));

        using var stream2 = ToStream("id,name\n1,Alice\n2,\n3,alberto\n");
        var nulls = await _sut.ReadAsync(stream2, 10, ",", true, 0, CancellationToken.None, [new FilterCondition("name", "isnull")]);
        Assert.Equal(["2"], nulls.Rows.Select(r => r["id"]));
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
