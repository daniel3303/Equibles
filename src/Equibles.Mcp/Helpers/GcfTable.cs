using System;
using System.Collections.Generic;
using System.Text;
using BlackwellSystems.Gcf;

namespace Equibles.Mcp.Helpers;

// Optional GCF (Graph Compact Format, https://gcformat.com) rendering for the MCP
// table tools. When EQUIBLES_OUTPUT_FORMAT=gcf, MarkdownTable.Render emits a GCF
// generic wire instead of a markdown table: the column names are factored into a
// single header and each row becomes one pipe-delimited line, dropping the per-cell
// "| " framing and the separator row. The exact rendered cell strings are reused, so
// every bit of the tools' number/price/date/em-dash formatting is preserved — GCF
// only changes the framing, not the values a model reads. Fewer tokens, losslessly.
public static class GcfTable
{
    // True when GCF output is requested. Read from the environment on each call so it
    // can be toggled per process (or per test) without a restart; the string compare is
    // negligible next to the data-access work behind every tool.
    public static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("EQUIBLES_OUTPUT_FORMAT")?.Trim(),
            "gcf",
            StringComparison.OrdinalIgnoreCase
        );

    // Encodes the markdown header + already-rendered data rows as a GCF generic wire,
    // or returns null to fall back to markdown. Null is returned whenever a row does not
    // split into the same number of cells as the header (a shape GCF would otherwise
    // misrepresent) or the encoder rejects the value, so enabling GCF never drops or
    // garbles a tool result.
    public static string TryEncode(string headerRow, IReadOnlyList<string> dataRows)
    {
        try
        {
            var headers = SplitCells(headerRow);
            if (headers.Count == 0)
                return null;

            var records = new List<object>(dataRows.Count);
            foreach (var row in dataRows)
            {
                var cells = SplitCells(row);
                if (cells.Count != headers.Count)
                    return null; // shape mismatch: keep the markdown table

                var map = new OrderedMap();
                for (var i = 0; i < headers.Count; i++)
                    map.Add(headers[i], cells[i]);
                records.Add(map);
            }

            return Gcf.EncodeGeneric(records);
        }
        catch
        {
            return null; // never fail a tool call over output formatting
        }
    }

    // Splits one markdown table row into trimmed, unescaped cell values. Columns are
    // delimited by "|"; MarkdownTable.EscapeCell doubles backslashes and writes data
    // pipes as "\|", so a pipe delimits a column only when preceded by an even number of
    // backslashes. The empty fields produced by the leading and trailing border pipes
    // are dropped.
    private static List<string> SplitCells(string row)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var backslashes = 0;

        foreach (var c in row)
        {
            if (c == '|' && backslashes % 2 == 0)
            {
                cells.Add(sb.ToString());
                sb.Clear();
                backslashes = 0;
                continue;
            }

            if (c == '\\')
                backslashes++;
            else
                backslashes = 0;

            sb.Append(c);
        }
        cells.Add(sb.ToString());

        if (cells.Count > 0 && cells[0].Trim().Length == 0)
            cells.RemoveAt(0);
        if (cells.Count > 0 && cells[cells.Count - 1].Trim().Length == 0)
            cells.RemoveAt(cells.Count - 1);

        for (var i = 0; i < cells.Count; i++)
            cells[i] = Unescape(cells[i].Trim());

        return cells;
    }

    // Reverses MarkdownTable.EscapeCell: "\|" -> "|" and "\\" -> "\".
    private static string Unescape(string cell)
    {
        if (cell.IndexOf('\\') < 0)
            return cell;

        var sb = new StringBuilder(cell.Length);
        for (var i = 0; i < cell.Length; i++)
        {
            if (
                cell[i] == '\\'
                && i + 1 < cell.Length
                && (cell[i + 1] == '|' || cell[i + 1] == '\\')
            )
            {
                sb.Append(cell[i + 1]);
                i++;
            }
            else
            {
                sb.Append(cell[i]);
            }
        }
        return sb.ToString();
    }
}
