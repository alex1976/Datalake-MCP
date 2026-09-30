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
/// When <c>contains</c> is given, only lines containing it (case-insensitive) are considered, and
/// offset/maxLines count those lines only.
/// </summary>
public sealed class TextReaderService
{
    public async Task<TextReadResult> ReadAsync(
        Stream stream, int maxLines, int offset, CancellationToken cancellationToken, string? contains = null)
    {
        using var reader = new StreamReader(stream);

        bool Matches(string line) =>
            string.IsNullOrEmpty(contains) || line.Contains(contains, StringComparison.OrdinalIgnoreCase);

        var skipped = 0;
        var lines = new List<string>();
        string? line;
        while (lines.Count < maxLines && (line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Matches(line))
            {
                continue;
            }

            if (skipped < offset)
            {
                skipped++;
                continue;
            }

            lines.Add(line);
        }

        // Peek ahead to the next matching line so Truncated (and NextOffset) only reflect real remaining
        // content, rather than assuming there is more just because maxLines was hit exactly.
        var truncated = false;
        if (lines.Count == maxLines)
        {
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (Matches(line))
                {
                    truncated = true;
                    break;
                }
            }
        }

        return new TextReadResult(lines, truncated, offset, truncated ? offset + lines.Count : null);
    }
}
