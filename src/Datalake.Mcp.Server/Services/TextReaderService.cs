namespace Datalake.Mcp.Server.Services;

public sealed record TextReadResult(
    IReadOnlyList<string> Lines,
    bool Truncated,
    int Offset,
    int? NextOffset);

/// <summary>
/// Reads plain-text content (txt, markdown, ...) line by line and stops as soon as
/// <paramref name="maxLines"/> is reached, so large files are not fully materialized in memory
/// just to preview a handful of lines. <paramref name="offset"/> lets callers page through a file
/// that is too large for a single response by skipping already-seen lines without materializing them.
/// </summary>
public sealed class TextReaderService
{
    public async Task<TextReadResult> ReadAsync(
        Stream stream, int maxLines, int offset, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);

        var skipped = 0;
        while (skipped < offset && await reader.ReadLineAsync(cancellationToken) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            skipped++;
        }

        var lines = new List<string>();
        string? line;
        while (lines.Count < maxLines && (line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lines.Add(line);
        }

        // Peek one line ahead so Truncated (and NextOffset) only reflect real remaining content,
        // rather than assuming there is more just because maxLines was hit exactly.
        var truncated = lines.Count == maxLines && await reader.ReadLineAsync(cancellationToken) is not null;

        return new TextReadResult(lines, truncated, offset, truncated ? offset + lines.Count : null);
    }
}
