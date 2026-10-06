using System.Text.Json;
using Datalake.Mcp.Server.Services;

namespace Datalake.Mcp.Server.Tests;

public class FileWriterServiceTests
{
    private readonly FileWriterService _sut = new();

    private static Dictionary<string, JsonElement> Row(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public async Task BuildParquetAsync_RoundTripsRowsAndInfersTypes()
    {
        var rows = new[]
        {
            Row("""{"id":1,"nome":"Rossi","importo":10.5,"attivo":true}"""),
            Row("""{"id":2,"nome":null,"importo":3,"attivo":false}"""),
            Row("""{"id":3,"importo":null}"""),
        };

        var bytes = await _sut.BuildParquetAsync(rows, CancellationToken.None);

        var reader = new ParquetReaderService();
        using var schemaStream = new MemoryStream(bytes);
        var schema = await reader.GetSchemaAsync(schemaStream, CancellationToken.None);
        Assert.Equal(["Int64", "String", "Double", "Boolean"], schema.Select(c => c.ClrType));

        using var stream = new MemoryStream(bytes);
        var result = await reader.ReadAsync(stream, 100, null, 0, CancellationToken.None);
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(1L, result.Rows[0]["id"]);
        Assert.Equal("Rossi", result.Rows[0]["nome"]);
        Assert.Null(result.Rows[1]["nome"]);
        Assert.Null(result.Rows[2]["attivo"]);
    }

    [Fact]
    public async Task BuildParquetAsync_RejectsEmptyRows()
        => await Assert.ThrowsAsync<ArgumentException>(() => _sut.BuildParquetAsync([], CancellationToken.None));

    [Fact]
    public void BuildCsv_AcceptsQuotedMultilineFields()
        => Assert.NotEmpty(_sut.BuildCsv("a,b\n1,\"x\ny\"\n", ","));

    [Fact]
    public void BuildCsv_RejectsInconsistentFieldCount()
        => Assert.Throws<ArgumentException>(() => _sut.BuildCsv("a,b\n1,2,3\n", ","));

    [Fact]
    public void BuildPdf_RequiresPdfHeader()
    {
        Assert.Throws<ArgumentException>(() => _sut.BuildPdf(Convert.ToBase64String("not a pdf"u8.ToArray())));
        Assert.Throws<ArgumentException>(() => _sut.BuildPdf("***"));
        Assert.NotEmpty(_sut.BuildPdf(Convert.ToBase64String("%PDF-1.7\n"u8.ToArray())));
    }

    [Theory]
    [InlineData("a\\b//c.txt", "a/b/c.txt")]
    [InlineData("/x/y.md", "x/y.md")]
    public void NormalizePath_CleansSeparators(string input, string expected)
        => Assert.Equal(expected, DataLakeService.NormalizePath(input));

    [Theory]
    [InlineData("../x.txt")]
    [InlineData("a/../b.txt")]
    [InlineData("/")]
    public void NormalizePath_RejectsTraversalAndEmpty(string input)
        => Assert.Throws<ArgumentException>(() => DataLakeService.NormalizePath(input));

    [Theory]
    [InlineData("CLIENT", null, new[] { "a/clienti.csv", "b/clienti.parquet" })]
    [InlineData("clienti", ".CSV", new[] { "a/clienti.csv" })]
    [InlineData(null, "markdown", new[] { "c/note.md" })]
    [InlineData("zzz", null, new string[0])]
    public void FileIndex_Search_FiltersByContainsAndFormat(string? name, string? format, string[] expected)
    {
        var t = DateTimeOffset.UtcNow;
        FileIndexEntry[] all =
        [
            new("a/clienti.csv", "clienti.csv", "csv", 1, t, null),
            new("b/clienti.parquet", "clienti.parquet", "parquet", 1, t, null),
            new("c/note.md", "note.md", "md", 1, t, null),
        ];

        Assert.Equal(expected, FileIndex.Search(all, name, format).Select(e => e.Path));
    }

    [Fact]
    public void FileIndex_UpsertReplacesSamePathAndKeepsOthers()
    {
        var t = DateTimeOffset.Parse("2026-10-06T10:00:00Z");
        var csv = FileIndex.Upsert(null, new("a/one.csv", "one.csv", "csv", 5, t, "con, virgola"));
        csv = FileIndex.Upsert(csv, new("a/two.md", "two.md", "md", 7, t, null));
        csv = FileIndex.Upsert(csv, new("A/ONE.csv", "ONE.csv", "csv", 9, t, "nuova"));

        var entries = FileIndex.Parse(csv);

        Assert.Equal(["a/two.md", "A/ONE.csv"], entries.Select(e => e.Path));
        Assert.Equal(9, entries[1].SizeBytes);
        Assert.StartsWith("path,name,format,sizeBytes,savedAt,description", csv);
    }
}
