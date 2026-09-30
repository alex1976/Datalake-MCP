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
/// data rows without materializing them. When <c>filters</c> are given, offset and maxRows count only
/// the rows matching all conditions.
/// </summary>
public sealed class CsvReaderService
{
    public async Task<CsvReadResult> ReadAsync(
        Stream stream, int maxRows, string delimiter, bool hasHeader, int offset, CancellationToken cancellationToken,
        IReadOnlyList<FilterCondition>? filters = null)
    {
        var filter = RowFilter.Create(filters);

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

        if (hasHeader)
        {
            filter?.EnsureColumnsExist(columns);
        }

        var filterValidated = hasHeader;
        var skipped = 0;
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        while (rows.Count < maxRows && await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasHeader && columns.Count == 0)
            {
                columns = Enumerable.Range(0, csv.Parser.Count).Select(i => $"column_{i}").ToList();
            }

            if (!filterValidated)
            {
                filter?.EnsureColumnsExist(columns);
                filterValidated = true;
            }

            if (filter is not null && !filter.Matches(name => GetField(csv, columns, name)))
            {
                continue;
            }

            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            var row = new Dictionary<string, string?>();
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = csv.GetField(i);
            }

            rows.Add(row);
        }

        // Peek ahead to the next matching row so Truncated (and NextOffset) only reflect real remaining
        // data, rather than assuming there is more just because maxRows was hit exactly.
        var truncated = false;
        if (rows.Count == maxRows)
        {
            while (await csv.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (filter is null || filter.Matches(name => GetField(csv, columns, name)))
                {
                    truncated = true;
                    break;
                }
            }
        }

        return new CsvReadResult(columns, rows, truncated, offset, truncated ? offset + rows.Count : null);
    }

    private static string? GetField(CsvReader csv, List<string> columns, string name)
    {
        var index = columns.FindIndex(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : csv.GetField(index);
    }
}
