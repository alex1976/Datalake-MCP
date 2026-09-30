using System.Text;
using Datalake.Mcp.Server.Services;

namespace Datalake.Mcp.Server.Tests;

public class TextReaderServiceTests
{
    private readonly TextReaderService _sut = new();

    [Fact]
    public async Task ReadAsync_ParsesLines()
    {
        using var stream = ToStream("line1\nline2\nline3\n");

        var result = await _sut.ReadAsync(stream, maxLines: 100, offset: 0, CancellationToken.None);

        Assert.Equal(["line1", "line2", "line3"], result.Lines);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_StopsAtMaxLinesAndReportsTruncation()
    {
        using var stream = ToStream("1\n2\n3\n4\n5\n");

        var result = await _sut.ReadAsync(stream, maxLines: 2, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Lines.Count);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_ExactlyMaxLines_DoesNotReportTruncation()
    {
        using var stream = ToStream("1\n2\n");

        var result = await _sut.ReadAsync(stream, maxLines: 2, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Lines.Count);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffset_SkipsAlreadyReturnedLines()
    {
        using var stream = ToStream("1\n2\n3\n4\n5\n");

        var result = await _sut.ReadAsync(stream, maxLines: 2, offset: 2, CancellationToken.None);

        Assert.Equal(2, result.Offset);
        Assert.Equal(["3", "4"], result.Lines);
        Assert.True(result.Truncated);
        Assert.Equal(4, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffsetPastEnd_ReturnsNoLines()
    {
        using var stream = ToStream("1\n2\n");

        var result = await _sut.ReadAsync(stream, maxLines: 100, offset: 10, CancellationToken.None);

        Assert.Empty(result.Lines);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_Contains_FiltersLinesAndPagesOverMatches()
    {
        using var stream = ToStream("foo 1\nbar\nFOO 2\nbaz\nfoo 3\n");

        var result = await _sut.ReadAsync(stream, maxLines: 1, offset: 1, CancellationToken.None, contains: "foo");

        Assert.Equal(["FOO 2"], result.Lines);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.NextOffset);
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
