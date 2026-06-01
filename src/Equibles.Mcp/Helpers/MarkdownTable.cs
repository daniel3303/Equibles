using System.Text;

namespace Equibles.Mcp.Helpers;

public static class MarkdownTable
{
    public static StringBuilder Start(string title, string headerRow, string separatorRow)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine();
        sb.AppendLine(headerRow);
        sb.AppendLine(separatorRow);
        return sb;
    }

    // The blank line between subtitle and header row is load-bearing: strict
    // CommonMark renderers need it to recognise the following rows as a table.
    public static StringBuilder Start(
        string title,
        string subtitle,
        string headerRow,
        string separatorRow
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(subtitle);
        sb.AppendLine();
        sb.AppendLine(headerRow);
        sb.AppendLine(separatorRow);
        return sb;
    }

    // Appends one markdown row per item in order, passing the 1-based rank and the item to
    // renderRow. Centralises the ranked-table loop so call sites don't repeat the `i + 1`
    // off-by-one and the `rows[i]` indexing.
    public static StringBuilder AppendNumberedRows<T>(
        this StringBuilder sb,
        IReadOnlyList<T> rows,
        Func<int, T, string> renderRow
    )
    {
        for (var i = 0; i < rows.Count; i++)
            sb.AppendLine(renderRow(i + 1, rows[i]));
        return sb;
    }
}
