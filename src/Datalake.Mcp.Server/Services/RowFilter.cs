using System.ComponentModel;
using System.Globalization;

namespace Datalake.Mcp.Server.Services;

/// <summary>A single column/operator/value predicate applied to tabular rows.</summary>
public sealed record FilterCondition(
    [property: Description("Nome della colonna su cui applicare il filtro (case-insensitive).")]
    string Column,
    [property: Description(
        "Operatore: eq (=), ne (!=), gt (>), gte (>=), lt (<), lte (<=), contains, startswith, endswith, " +
        "isnull, isnotnull. I confronti sono numerici se cella e valore sono numeri, altrimenti testuali " +
        "case-insensitive. isnull/isnotnull non richiedono 'value' (una stringa vuota conta come null).")]
    string Operator,
    [property: Description("Valore con cui confrontare (numeri e date in formato invariante, es. 12.5 o 2024-01-31).")]
    string? Value = null);

internal enum FilterOperator
{
    Eq, Ne, Gt, Gte, Lt, Lte, Contains, StartsWith, EndsWith, IsNull, IsNotNull,
}

/// <summary>
/// A validated set of <see cref="FilterCondition"/>s combined with AND. Cells are looked up through a
/// caller-supplied accessor so the same evaluation logic serves CSV, Parquet and XML rows.
/// </summary>
internal sealed class RowFilter
{
    private readonly (string Column, FilterOperator Operator, string? Value)[] _conditions;

    private RowFilter((string, FilterOperator, string?)[] conditions) => _conditions = conditions;

    public IEnumerable<string> Columns => _conditions.Select(c => c.Column);

    /// <summary>Returns null when there is nothing to filter on.</summary>
    public static RowFilter? Create(IReadOnlyList<FilterCondition>? filters)
    {
        if (filters is not { Count: > 0 })
        {
            return null;
        }

        var conditions = filters.Select(f =>
        {
            if (string.IsNullOrWhiteSpace(f.Column))
            {
                throw new ArgumentException("Ogni filtro deve specificare una colonna.");
            }

            var op = ParseOperator(f.Operator);
            if (op is not (FilterOperator.IsNull or FilterOperator.IsNotNull) && f.Value is null)
            {
                throw new ArgumentException($"L'operatore '{f.Operator}' richiede un 'value' (colonna '{f.Column}').");
            }

            return (f.Column, op, f.Value);
        }).ToArray();

        return new RowFilter(conditions);
    }

    /// <summary>
    /// Throws if a filter references a column that is not in <paramref name="available"/>, listing the valid ones.
    /// </summary>
    public void EnsureColumnsExist(IEnumerable<string> available)
    {
        var known = available.ToList();
        foreach (var column in Columns)
        {
            if (!known.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Colonna di filtro '{column}' non trovata. Colonne disponibili: {string.Join(", ", known)}.");
            }
        }
    }

    public bool Matches(Func<string, object?> getValue)
    {
        foreach (var (column, op, value) in _conditions)
        {
            if (!Evaluate(getValue(column), op, value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Evaluate(object? cell, FilterOperator op, string? value)
    {
        var text = ToInvariantString(cell);

        switch (op)
        {
            case FilterOperator.IsNull:
                return string.IsNullOrEmpty(text);
            case FilterOperator.IsNotNull:
                return !string.IsNullOrEmpty(text);
        }

        if (text is null)
        {
            // A null cell matches nothing except "not equal".
            return op == FilterOperator.Ne;
        }

        switch (op)
        {
            case FilterOperator.Contains:
                return text.Contains(value!, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.StartsWith:
                return text.StartsWith(value!, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.EndsWith:
                return text.EndsWith(value!, StringComparison.OrdinalIgnoreCase);
        }

        var comparison = Compare(cell, text, value!);
        return op switch
        {
            FilterOperator.Eq => comparison == 0,
            FilterOperator.Ne => comparison != 0,
            FilterOperator.Gt => comparison > 0,
            FilterOperator.Gte => comparison >= 0,
            FilterOperator.Lt => comparison < 0,
            FilterOperator.Lte => comparison <= 0,
            _ => false,
        };
    }

    private static int Compare(object? cell, string cellText, string value)
    {
        if (cell is DateTime or DateTimeOffset)
        {
            var cellDate = cell is DateTimeOffset dto ? dto : new DateTimeOffset(DateTime.SpecifyKind((DateTime)cell, DateTimeKind.Utc));
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var valueDate))
            {
                return cellDate.CompareTo(valueDate);
            }
        }

        if (decimal.TryParse(cellText, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var n1)
            && decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var n2))
        {
            return n1.CompareTo(n2);
        }

        return string.Compare(cellText, value, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ToInvariantString(object? cell) => cell switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => cell.ToString(),
    };

    private static FilterOperator ParseOperator(string? op) => op?.Trim().ToLowerInvariant() switch
    {
        "eq" or "=" or "==" => FilterOperator.Eq,
        "ne" or "!=" or "<>" => FilterOperator.Ne,
        "gt" or ">" => FilterOperator.Gt,
        "gte" or ">=" => FilterOperator.Gte,
        "lt" or "<" => FilterOperator.Lt,
        "lte" or "<=" => FilterOperator.Lte,
        "contains" => FilterOperator.Contains,
        "startswith" => FilterOperator.StartsWith,
        "endswith" => FilterOperator.EndsWith,
        "isnull" => FilterOperator.IsNull,
        "isnotnull" => FilterOperator.IsNotNull,
        _ => throw new ArgumentException(
            $"Operatore di filtro non valido '{op}'. Valori ammessi: eq, ne, gt, gte, lt, lte, contains, startswith, endswith, isnull, isnotnull."),
    };
}
