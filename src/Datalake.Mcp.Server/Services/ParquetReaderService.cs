using Parquet;
using Parquet.Serialization;

namespace Datalake.Mcp.Server.Services;

public sealed record ParquetColumnInfo(string Name, string ClrType, bool IsNullable);

public sealed record ParquetReadResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated,
    long TotalRowCount,
    long Offset,
    long? NextOffset);

/// <summary>
/// Reads Parquet files row-group by row-group so callers can inspect the schema, or pull a bounded preview,
/// without materializing the full dataset in memory. <paramref name="offset"/> lets callers page through a
/// file that is too large for a single response: rows before the offset are discarded as they are
/// deserialized rather than being returned.
/// </summary>
public sealed class ParquetReaderService
{
    public async Task<IReadOnlyList<ParquetColumnInfo>> GetSchemaAsync(Stream stream, CancellationToken cancellationToken)
    {
        var schema = await ParquetReader.ReadSchemaAsync(stream);
        return schema.GetDataFields()
            .Select(f => new ParquetColumnInfo(f.Name, f.ClrType.Name, f.IsNullable))
            .ToList();
    }

    public async Task<ParquetReadResult> ReadAsync(
        Stream stream, int maxRows, IReadOnlyList<string>? columns, long offset, CancellationToken cancellationToken,
        IReadOnlyList<FilterCondition>? filters = null)
    {
        var filter = RowFilter.Create(filters);

        int rowGroupCount;
        IReadOnlyList<string> allColumnNames;
        await using (var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken))
        {
            rowGroupCount = reader.RowGroupCount;
            allColumnNames = reader.Schema.GetDataFields().Select(f => f.Name).ToList();
        }

        filter?.EnsureColumnsExist(allColumnNames);

        var projectedColumns = columns is { Count: > 0 }
            ? allColumnNames.Where(c => columns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
            : allColumnNames;

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        var truncated = false;
        long totalRowCount = 0;
        long globalRowIndex = 0;

        for (var rowGroup = 0; rowGroup < rowGroupCount; rowGroup++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ParquetSerializer.DeserializeUntypedAsync(stream, rowGroupIndex: rowGroup, cancellationToken: cancellationToken);
            totalRowCount += result.Data.Count;

            foreach (var record in result.Data)
            {
                // Filter columns need not be projected: the record still carries every column.
                // globalRowIndex then counts matching rows only, so offset pages over the filtered set.
                if (filter is not null && !filter.Matches(name => GetValue(record, name)))
                {
                    continue;
                }

                if (globalRowIndex < offset)
                {
                    globalRowIndex++;
                    continue;
                }

                globalRowIndex++;

                if (truncated)
                {
                    continue;
                }

                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    continue;
                }

                var row = new Dictionary<string, object?>();
                foreach (var column in projectedColumns)
                {
                    row[column] = record.TryGetValue(column, out var value) ? value : null;
                }

                rows.Add(row);
            }
        }

        return new ParquetReadResult(projectedColumns, rows, truncated, totalRowCount, offset, truncated ? offset + rows.Count : null);
    }

    private static object? GetValue(IDictionary<string, object> record, string name)
    {
        if (record.TryGetValue(name, out var value))
        {
            return value;
        }

        return record.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
