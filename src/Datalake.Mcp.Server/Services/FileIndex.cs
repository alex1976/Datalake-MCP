using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Datalake.Mcp.Server.Services;

public sealed record FileIndexEntry(
    string Path, string Name, string Format, long SizeBytes, DateTimeOffset SavedAt, string? Description);

/// <summary>
/// Serializes the index of files saved through the MCP server as a CSV (readable with read_csv): one row per
/// file path, so saving the same path again replaces its row instead of duplicating it.
/// </summary>
public static class FileIndex
{
    private static readonly string[] Header = ["path", "name", "format", "sizeBytes", "savedAt", "description"];

    private static readonly CsvConfiguration Config = new(CultureInfo.InvariantCulture) { HasHeaderRecord = true };

    public static string Upsert(string? existingCsv, FileIndexEntry entry)
    {
        var entries = Parse(existingCsv);
        entries.RemoveAll(e => string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        entries.Add(entry);
        return Serialize(entries);
    }

    public static List<FileIndexEntry> Parse(string? csv)
    {
        var entries = new List<FileIndexEntry>();
        if (string.IsNullOrWhiteSpace(csv))
        {
            return entries;
        }

        using var reader = new CsvReader(new StringReader(csv), Config);
        reader.Read();
        reader.ReadHeader();
        while (reader.Read())
        {
            entries.Add(new FileIndexEntry(
                reader.GetField("path") ?? string.Empty,
                reader.GetField("name") ?? string.Empty,
                reader.GetField("format") ?? string.Empty,
                long.TryParse(reader.GetField("sizeBytes"), CultureInfo.InvariantCulture, out var size) ? size : 0,
                DateTimeOffset.TryParse(reader.GetField("savedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedAt) ? savedAt : default,
                reader.GetField("description")));
        }

        return entries;
    }

    /// <summary>Entries whose name contains <paramref name="name"/> (case-insensitive) and whose format matches <paramref name="format"/>; both filters optional.</summary>
    public static List<FileIndexEntry> Search(IEnumerable<FileIndexEntry> entries, string? name, string? format)
    {
        var wantedFormat = NormalizeFormat(format);
        return entries
            .Where(e => string.IsNullOrWhiteSpace(name) || e.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(e => wantedFormat is null || string.Equals(e.Format, wantedFormat, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? NormalizeFormat(string? format)
    {
        var f = format?.Trim().TrimStart('.').ToLowerInvariant();
        return f switch { null or "" => null, "markdown" => "md", "text" => "txt", _ => f };
    }

    private static string Serialize(IEnumerable<FileIndexEntry> entries)
    {
        var sb = new StringBuilder();
        using (var writer = new CsvWriter(new StringWriter(sb), Config))
        {
            foreach (var column in Header)
            {
                writer.WriteField(column);
            }

            writer.NextRecord();

            foreach (var e in entries)
            {
                writer.WriteField(e.Path);
                writer.WriteField(e.Name);
                writer.WriteField(e.Format);
                writer.WriteField(e.SizeBytes.ToString(CultureInfo.InvariantCulture));
                writer.WriteField(e.SavedAt.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteField(e.Description);
                writer.NextRecord();
            }
        }

        return sb.ToString();
    }
}

/// <summary>Esito di search_file: file trovati e, se unico e leggibile, il suo contenuto (primo blocco di righe).</summary>
public sealed record SearchFileResult(IReadOnlyList<FileIndexEntry> Files, object? Content, string? Message);
