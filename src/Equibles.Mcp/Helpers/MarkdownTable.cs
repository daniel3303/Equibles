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
}
