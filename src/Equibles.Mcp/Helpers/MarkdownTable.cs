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

    // Renders rows as a markdown table, or returns emptyMessage verbatim when there are none.
    // Centralises the search-result table shape (empty check → header → one row per item)
    // repeated across the MCP search tools. Callers materialise the query first so this helper
    // stays free of any data-access dependency.
    public static string Render<T>(
        IReadOnlyList<T> rows,
        string emptyMessage,
        string title,
        string headerRow,
        string separatorRow,
        Func<T, string> renderRow
    )
    {
        if (rows.Count == 0)
            return emptyMessage;

        var sb = Start(title, headerRow, separatorRow);
        foreach (var row in rows)
            sb.AppendLine(renderRow(row));
        return sb.ToString();
    }

    // Subtitle-carrying variant of Render: same empty-check and one-row-per-item shape,
    // but renders the title + subtitle header (with the load-bearing blank line) for the
    // tools that show a "Showing N of M" line above the table.
    public static string Render<T>(
        IReadOnlyList<T> rows,
        string emptyMessage,
        string title,
        string subtitle,
        string headerRow,
        string separatorRow,
        Func<T, string> renderRow
    )
    {
        if (rows.Count == 0)
            return emptyMessage;

        var sb = Start(title, subtitle, headerRow, separatorRow);
        foreach (var row in rows)
            sb.AppendLine(renderRow(row));
        return sb.ToString();
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
