using System.Xml;
using System.Xml.Linq;

namespace Datalake.Mcp.Server.Services;

public sealed record XmlColumnInfo(string Name, bool IsAttribute);

public sealed record XmlReadResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    bool Truncated,
    int Offset,
    int? NextOffset);

/// <summary>
/// Reads tabular XML (a root element containing repeated record elements, e.g. &lt;Root&gt;&lt;Record&gt;...&lt;/Record&gt;...&lt;/Root&gt;)
/// one record at a time via a forward-only <see cref="XmlReader"/>, so large files are not fully materialized
/// in memory. The record element is auto-detected as the root's direct children unless <c>recordElement</c> is
/// given. Attributes and child elements of each record become columns. <paramref name="offset"/>-style paging
/// mirrors <see cref="CsvReaderService"/>/<see cref="ParquetReaderService"/>: records before the offset are
/// discarded as they are read rather than being materialized into the result.
/// DTD processing and external entity resolution are disabled to avoid XXE when parsing untrusted files.
/// </summary>
public sealed class XmlReaderService
{
    public async Task<IReadOnlyList<XmlColumnInfo>> GetSchemaAsync(
        Stream stream, string? recordElement, CancellationToken cancellationToken)
    {
        using var reader = CreateReader(stream);
        await reader.MoveToContentAsync();

        var record = await ReadNextMatchingRecordAsync(reader, recordElement, cancellationToken);
        return record is null ? [] : BuildColumns(record);
    }

    public async Task<XmlReadResult> ReadAsync(
        Stream stream, int maxRows, string? recordElement, IReadOnlyList<string>? columns, int offset, CancellationToken cancellationToken,
        IReadOnlyList<FilterCondition>? filters = null)
    {
        var filter = RowFilter.Create(filters);

        using var reader = CreateReader(stream);
        await reader.MoveToContentAsync();

        IReadOnlyList<string>? projectedColumns = columns is { Count: > 0 } ? columns : null;
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        var filterValidated = false;
        var skipped = 0;

        XElement? record;
        while (rows.Count < maxRows && (record = await ReadNextMatchingRecordAsync(reader, recordElement, cancellationToken)) is not null)
        {
            if (!filterValidated)
            {
                filter?.EnsureColumnsExist(BuildColumns(record).Select(c => c.Name));
                filterValidated = true;
            }

            if (filter is not null && !filter.Matches(name => GetValue(record, name)))
            {
                continue;
            }

            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            projectedColumns ??= BuildColumns(record).Select(c => c.Name).ToList();
            rows.Add(BuildRow(record, projectedColumns));
        }

        // Peek ahead to the next matching record so Truncated (and NextOffset) only reflect real remaining
        // data, rather than assuming there is more just because maxRows was hit exactly.
        var truncated = false;
        if (rows.Count == maxRows)
        {
            while ((record = await ReadNextMatchingRecordAsync(reader, recordElement, cancellationToken)) is not null)
            {
                if (filter is null || filter.Matches(name => GetValue(record, name)))
                {
                    truncated = true;
                    break;
                }
            }
        }

        return new XmlReadResult(projectedColumns ?? [], rows, truncated, offset, truncated ? offset + rows.Count : null);
    }

    private static string? GetValue(XElement record, string column)
    {
        var attribute = record.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, column, StringComparison.OrdinalIgnoreCase));
        if (attribute is not null)
        {
            return attribute.Value;
        }

        return record.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, column, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static async Task<XElement?> ReadNextMatchingRecordAsync(
        XmlReader reader, string? recordElement, CancellationToken cancellationToken)
    {
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                return null;
            }

            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (recordElement is not null && !string.Equals(reader.LocalName, recordElement, StringComparison.OrdinalIgnoreCase))
            {
                // Consume (and discard) the non-matching subtree the same way a matched record is
                // built, so the reader ends up positioned on its EndElement either way: Skip() would
                // instead leave it already on the *following* sibling, causing the next ReadAsync()
                // at the top of this loop to silently skip that sibling too.
                await BuildElementAsync(reader, cancellationToken);
                continue;
            }

            return await BuildElementAsync(reader, cancellationToken);
        }

        return null;
    }

    private static async Task<XElement> BuildElementAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        var element = new XElement(reader.LocalName);

        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                if (!reader.Name.StartsWith("xmlns", StringComparison.Ordinal))
                {
                    element.SetAttributeValue(reader.LocalName, reader.Value);
                }
            }

            reader.MoveToElement();
        }

        if (reader.IsEmptyElement)
        {
            return element;
        }

        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    element.Add(await BuildElementAsync(reader, cancellationToken));
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    element.Add(new XText(reader.Value));
                    break;
                case XmlNodeType.EndElement:
                    return element;
            }
        }

        return element;
    }

    private static List<XmlColumnInfo> BuildColumns(XElement record)
    {
        var columns = new List<XmlColumnInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in record.Attributes())
        {
            if (seen.Add(attribute.Name.LocalName))
            {
                columns.Add(new XmlColumnInfo(attribute.Name.LocalName, IsAttribute: true));
            }
        }

        foreach (var child in record.Elements())
        {
            if (seen.Add(child.Name.LocalName))
            {
                columns.Add(new XmlColumnInfo(child.Name.LocalName, IsAttribute: false));
            }
        }

        return columns;
    }

    private static Dictionary<string, string?> BuildRow(XElement record, IReadOnlyList<string> columns)
    {
        var row = new Dictionary<string, string?>();

        foreach (var column in columns)
        {
            var attribute = record.Attributes()
                .FirstOrDefault(a => string.Equals(a.Name.LocalName, column, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null)
            {
                row[column] = attribute.Value;
                continue;
            }

            var element = record.Elements()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, column, StringComparison.OrdinalIgnoreCase));
            row[column] = element?.Value;
        }

        return row;
    }

    private static XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        Async = true,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    });
}
