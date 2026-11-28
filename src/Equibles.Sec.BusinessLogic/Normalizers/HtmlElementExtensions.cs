using AngleSharp.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class HtmlElementExtensions
{
    internal static List<IElement> DirectChildCells(IElement row)
    {
        return row.Children.Where(c => c.LocalName is "td" or "th").ToList();
    }

    internal static void InsertAfter(INode parent, INode newNode, INode refNode)
    {
        parent.InsertBefore(newNode, refNode.NextSibling);
    }

    // Filers pad empty cells with zero-width marks as often as with spaces; neither is content.
    internal static bool IsBlankText(string text) => text == null || text.All(IsBlankChar);

    internal static string TrimBlank(string text)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && IsBlankChar(text[start]))
            start++;
        while (end > start && IsBlankChar(text[end - 1]))
            end--;
        return text[start..end];
    }

    private static bool IsBlankChar(char c) => char.IsWhiteSpace(c) || ZeroWidthMarks.Contains(c);

    private const string ZeroWidthMarks = "\u200B\u200C\u200D\u2060\uFEFF";
}
