using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Datalake.Mcp.Server.Services;

public sealed record CsvReadResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    bool Truncated,
    int Offset,
    int? NextOffset);

/// <summary>
/// Reads CSV content row by row and stops as soon as <paramref name="maxRows"/> is reached, so large
/// files are not fully materialized in memory just to preview a handful of rows. <paramref name="offset"/>
/// lets callers page through a file that is too large for a single response by skipping already-seen
/// data rows without materializing them.
/// </summary>
public sealed class CsvReaderService
{
    public async Task<CsvReadResult> ReadAsync(
        Stream stream, int maxRows, string delimiter, bool hasHeader, int offset, CancellationToken cancellationToken)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            HasHeaderRecord = hasHeader,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);

        if (hasHeader)
        {
            await csv.ReadAsync();
            csv.ReadHeader();
        }

        var columns = hasHeader
            ? csv.HeaderRecord?.ToList() ?? []
            : [];

        var skipped = 0;
        while (skipped < offset && await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasHeader && columns.Count == 0)
            {
                columns = Enumerable.Range(0, csv.Parser.Count).Select(i => $"column_{i}").ToList();
            }

            skipped++;
        }

        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (rows.Count < maxRows && await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasHeader && columns.Count == 0)
            {
                columns = Enumerable.Range(0, csv.Parser.Count).Select(i => $"column_{i}").ToList();
            }

            var row = new Dictionary<string, string?>();
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = csv.GetField(i);
            }

            rows.Add(row);
        }

        // Peek one row ahead so Truncated (and NextOffset) only reflect real remaining data,
        // rather than assuming there is more just because maxRows was hit exactly.
        var truncated = rows.Count == maxRows && await csv.ReadAsync();

        return new CsvReadResult(columns, rows, truncated, offset, truncated ? offset + rows.Count : null);
    }
}
