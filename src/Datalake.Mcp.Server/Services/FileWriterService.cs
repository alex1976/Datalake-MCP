using System.Globalization;
using System.Text;
using System.Text.Json;
using Parquet.Schema;
using Parquet.Serialization;

namespace Datalake.Mcp.Server.Services;

/// <summary>
/// Builds the byte payload for each supported output format. Kept separate from the Data Lake upload so the
/// validation/serialization logic can be tested without any network access.
/// </summary>
public sealed class FileWriterService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public byte[] BuildText(string content) => Utf8NoBom.GetBytes(content);

    public byte[] BuildCsv(string content, string delimiter)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Il contenuto CSV non può essere vuoto.", nameof(content));
        }

        if (string.IsNullOrEmpty(delimiter))
        {
            throw new ArgumentException("Il delimitatore CSV non può essere vuoto.", nameof(delimiter));
        }

        // Every non-blank line must be parsable as a record with the same number of fields as the header
        // (quoted fields may span lines), so a malformed CSV is rejected rather than persisted.
        var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var reader = new CsvHelper.CsvReader(new StringReader(content), config);
        var expectedFields = -1;
        var row = 0;
        while (reader.Read())
        {
            row++;
            var count = reader.Parser.Count;
            if (expectedFields < 0)
            {
                expectedFields = count;
            }
            else if (count != expectedFields)
            {
                throw new ArgumentException(
                    $"CSV non valido: la riga {row} ha {count} campi, ne erano attesi {expectedFields}.", nameof(content));
            }
        }

        return Utf8NoBom.GetBytes(content);
    }

    public byte[] BuildPdf(string contentBase64)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Il contenuto PDF non è una stringa base64 valida.", nameof(contentBase64), ex);
        }

        // "%PDF-" magic number: rejects arbitrary binary payloads saved under a .pdf name.
        ReadOnlySpan<byte> magic = "%PDF-"u8;
        if (bytes.Length < magic.Length || !bytes.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new ArgumentException("Il contenuto non è un PDF valido (manca l'intestazione %PDF-).", nameof(contentBase64));
        }

        return bytes;
    }

    /// <summary>
    /// Serializes rows (column name → JSON value) into a single-row-group Parquet file. Each column's type is
    /// inferred from its values: bool, long, double, or string as fallback; all columns are nullable.
    /// </summary>
    public async Task<byte[]> BuildParquetAsync(
        IReadOnlyList<Dictionary<string, JsonElement>> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("Serve almeno una riga per creare un file Parquet.", nameof(rows));
        }

        var columnNames = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (!columnNames.Contains(key, StringComparer.Ordinal))
                {
                    columnNames.Add(key);
                }
            }
        }

        if (columnNames.Count == 0 || columnNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Le righe devono avere almeno una colonna con nome non vuoto.", nameof(rows));
        }

        var fields = new List<DataField>();
        var converters = new List<Func<JsonElement, object?>>();
        foreach (var name in columnNames)
        {
            var values = rows.Select(r => r.TryGetValue(name, out var v) ? v : default).ToList();
            var (field, convert) = InferColumn(name, values);
            fields.Add(field);
            converters.Add(convert);
        }

        var data = rows
            .Select(row =>
            {
                var record = new Dictionary<string, object?>(columnNames.Count);
                for (var i = 0; i < columnNames.Count; i++)
                {
                    var value = row.TryGetValue(columnNames[i], out var v) ? converters[i](v) : null;
                    if (value is not null)
                    {
                        record[columnNames[i]] = value;
                    }
                }

                return (IDictionary<string, object?>)record;
            })
            .ToList();

        using var stream = new MemoryStream();
        await ParquetSerializer.SerializeUntypedAsync(data, new ParquetSchema(fields), stream, cancellationToken: cancellationToken);
        return stream.ToArray();
    }

    private static (DataField Field, Func<JsonElement, object?> Convert) InferColumn(string name, List<JsonElement> values)
    {
        // default(JsonElement) has ValueKind Undefined: a row that omits the column is treated as null.
        var present = values.Where(v => !IsNull(v)).ToList();

        if (present.Count > 0 && present.All(v => v.ValueKind is JsonValueKind.True or JsonValueKind.False))
        {
            return (new DataField<bool?>(name), v => IsNull(v) ? null : v.GetBoolean());
        }

        if (present.Count > 0 && present.All(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _)))
        {
            return (new DataField<long?>(name), v => IsNull(v) ? null : v.GetInt64());
        }

        if (present.Count > 0 && present.All(v => v.ValueKind == JsonValueKind.Number))
        {
            return (new DataField<double?>(name), v => IsNull(v) ? null : v.GetDouble());
        }

        return (new DataField<string>(name),
            v => IsNull(v) ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText());
    }

    private static bool IsNull(JsonElement v) => v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
}
