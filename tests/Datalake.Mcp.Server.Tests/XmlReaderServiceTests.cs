using System.Text;
using Datalake.Mcp.Server.Services;

namespace Datalake.Mcp.Server.Tests;

public class XmlReaderServiceTests
{
    private readonly XmlReaderService _sut = new();

    private const string SampleXml =
        """
        <Products>
          <Product id="1"><Name>Alice</Name><Price>10</Price></Product>
          <Product id="2"><Name>Bob</Name><Price>20</Price></Product>
          <Product id="3"><Name>Carl</Name><Price>30</Price></Product>
        </Products>
        """;

    [Fact]
    public async Task GetSchemaAsync_DetectsAttributesAndElementsFromFirstRecord()
    {
        using var stream = ToStream(SampleXml);

        var schema = await _sut.GetSchemaAsync(stream, recordElement: null, CancellationToken.None);

        Assert.Equal(["id", "Name", "Price"], schema.Select(c => c.Name));
        Assert.True(schema.Single(c => c.Name == "id").IsAttribute);
        Assert.False(schema.Single(c => c.Name == "Name").IsAttribute);
    }

    [Fact]
    public async Task ReadAsync_ParsesAttributesAndElementsAsColumns()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 100, recordElement: null, columns: null, offset: 0, CancellationToken.None);

        Assert.Equal(["id", "Name", "Price"], result.Columns);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("1", result.Rows[0]["id"]);
        Assert.Equal("Alice", result.Rows[0]["Name"]);
        Assert.Equal("10", result.Rows[0]["Price"]);
        Assert.False(result.Truncated);
        Assert.Null(result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_StopsAtMaxRowsAndReportsTruncation()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 2, recordElement: null, columns: null, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.NextOffset);
    }

    [Fact]
    public async Task ReadAsync_WithOffset_SkipsAlreadyReturnedRecords()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 100, recordElement: null, columns: null, offset: 1, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Bob", result.Rows[0]["Name"]);
        Assert.Equal("Carl", result.Rows[1]["Name"]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task ReadAsync_WithRecordElement_FiltersByLocalName()
    {
        const string xml =
            """
            <Catalog>
              <Metadata><Owner>Acme</Owner></Metadata>
              <Product id="1"><Name>Alice</Name></Product>
              <Product id="2"><Name>Bob</Name></Product>
            </Catalog>
            """;
        using var stream = ToStream(xml);

        var result = await _sut.ReadAsync(stream, maxRows: 100, recordElement: "Product", columns: null, offset: 0, CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Alice", result.Rows[0]["Name"]);
        Assert.Equal("Bob", result.Rows[1]["Name"]);
    }

    [Fact]
    public async Task ReadAsync_WithColumnsProjection_ReturnsOnlyRequestedColumns()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 100, recordElement: null, columns: ["Name"], offset: 0, CancellationToken.None);

        Assert.Equal(["Name"], result.Columns);
        Assert.Equal(["Alice", "Bob", "Carl"], result.Rows.Select(r => r["Name"]));
    }

    [Fact]
    public async Task ReadAsync_Filter_MatchesAttributesAndElements()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 100, recordElement: null, columns: ["Name"], offset: 0, CancellationToken.None,
            [new FilterCondition("Price", "gte", "20"), new FilterCondition("id", "ne", "3")]);

        Assert.Equal(["Bob"], result.Rows.Select(r => r["Name"]));
    }

    [Fact]
    public async Task ReadAsync_Filter_OffsetAndTruncationCountMatchingRecordsOnly()
    {
        using var stream = ToStream(SampleXml);

        var result = await _sut.ReadAsync(stream, maxRows: 1, recordElement: null, columns: ["Name"], offset: 0, CancellationToken.None,
            [new FilterCondition("Price", "gt", "10")]);

        Assert.Equal(["Bob"], result.Rows.Select(r => r["Name"]));
        Assert.True(result.Truncated);
        Assert.Equal(1, result.NextOffset);
    }

    private static MemoryStream ToStream(string content) => new(Encoding.UTF8.GetBytes(content));
}
