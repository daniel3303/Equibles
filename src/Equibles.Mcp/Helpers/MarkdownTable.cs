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
}
