using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;

/// <summary>
/// One Markdown table of a stored earnings release: the header rows above its first data row,
/// and each data row as a label plus the numeric cells it states.
/// </summary>
public sealed partial class ReleaseTable
{
    private const int MinTableLines = 4;

    public List<string[]> HeaderRows { get; } = [];
    public List<ReleaseTableRow> Rows { get; } = [];

    [GeneratedRegex(@"^\(?-?\$?\s*\(?\s*([0-9][0-9,]*(?:\.[0-9]+)?)\s*\)?$")]
    private static partial Regex NumberPattern();

    [GeneratedRegex("[A-Za-z]{3}")]
    private static partial Regex WordPattern();

    public static List<ReleaseTable> ReadAll(string text)
    {
        var tables = new List<ReleaseTable>();
        var block = new List<(int Line, string[] Cells)>();
        var lines = text.Split('\n');
        for (var i = 0; i <= lines.Length; i++)
        {
            var line = i < lines.Length ? lines[i] : string.Empty;
            if (line.TrimStart().StartsWith('|'))
            {
                block.Add((i + 1, line.Trim().Trim('|').Split('|')));
                continue;
            }

            if (block.Count >= MinTableLines)
                tables.Add(FromBlock(block));
            block.Clear();
        }

        return tables;
    }

    private static ReleaseTable FromBlock(List<(int Line, string[] Cells)> block)
    {
        var table = new ReleaseTable();
        foreach (var (line, cells) in block)
        {
            if (cells.All(IsSeparator))
                continue;

            string label = null;
            var values = new Dictionary<int, decimal>();
            for (var i = 0; i < cells.Length; i++)
            {
                if (TryParseNumber(cells[i], out var value))
                    values[i] = value;
                else if (label == null && WordPattern().IsMatch(cells[i]))
                    label = Clean(cells[i]);
            }

            // A row of bare years is the header's year line, not figures.
            var bareYears =
                values.Count > 0
                && values.Values.All(v => v >= 1990 && v <= 2100 && v == decimal.Truncate(v));
            if (label != null && values.Count > 0 && !bareYears)
                table.Rows.Add(new ReleaseTableRow(label, values, cells, line));
            else if (table.Rows.Count == 0)
                table.HeaderRows.Add(cells);
        }

        return table;
    }

    /// <summary>
    /// The header text over a column: per header row, the last non-empty cell at or left of the
    /// column, since a spanning header cell keeps only its first index in Markdown.
    /// </summary>
    public string HeaderOver(int column)
    {
        var parts = new List<string>();
        foreach (var cells in HeaderRows)
        {
            var text = string.Empty;
            for (var i = Math.Min(column, cells.Length - 1); i >= 0; i--)
            {
                var cell = Clean(cells[i]);
                if (cell.Length == 0)
                    continue;
                text = cell;
                break;
            }

            parts.Add(text);
        }

        return string.Join(' ', parts);
    }

    public static string Clean(string cell) =>
        cell.Trim()
            .Replace("**", string.Empty)
            .Replace('—', '-')
            .Replace('–', '-')
            .Replace(' ', ' ')
            .Trim();

    public static bool HasDecimals(string cell) => Clean(cell).Contains('.');

    private static bool IsSeparator(string cell) => cell.All(c => c is '-' or ':' or ' ');

    private static bool TryParseNumber(string cell, out decimal value)
    {
        value = 0;
        var text = Clean(cell);
        if (text.Length == 0 || text is "$" or ")" or "%" or "-" || text.EndsWith('%'))
            return false;

        var match = NumberPattern().Match(text);
        if (!match.Success)
            return false;
        if (
            !decimal.TryParse(
                match.Groups[1].Value.Replace(",", string.Empty),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var magnitude
            )
        )
            return false;

        var negative =
            text.StartsWith('(')
            || text.StartsWith('-')
            || text.StartsWith("$(", StringComparison.Ordinal)
            || text.StartsWith("$ (", StringComparison.Ordinal)
            || text.StartsWith("$-", StringComparison.Ordinal);
        value = negative ? -magnitude : magnitude;
        return true;
    }
}

public sealed record ReleaseTableRow(
    string Label,
    IReadOnlyDictionary<int, decimal> Values,
    string[] Cells,
    int Line
);
