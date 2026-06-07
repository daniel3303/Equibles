using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class PaginationRemovalStep : IHtmlNormalizationStep
{
    public void Execute(IHtmlDocument doc)
    {
        var hrElements = doc.Body?.QuerySelectorAll(":scope > hr")?.ToList();
        if (hrElements == null || hrElements.Count == 0)
            return;

        foreach (var hr in hrElements)
        {
            var elementsToRemove = new List<INode> { hr };

            // Check element before HR for page number
            var previousSibling = FindFirstMeaningfulSibling(hr, forward: false);
            if (previousSibling != null && int.TryParse(previousSibling.TextContent.Trim(), out _))
            {
                elementsToRemove.Add(previousSibling);
            }

            // Check element after HR for Part header
            var nextSibling = FindFirstMeaningfulSibling(hr, forward: true);
            if (nextSibling != null && IsPartHeader(nextSibling.TextContent))
            {
                elementsToRemove.Add(nextSibling);
            }

            foreach (var element in elementsToRemove)
            {
                element.Parent?.RemoveChild(element);
            }
        }
    }

    // Treat the after-HR sibling as a Part header only when "Part" is followed by a
    // whitespace boundary AND a roman-numeral identifier — so ordinary words that merely
    // start with "Part" (Partnership, …) and prose sentences beginning "Part of …" are not
    // mistaken for a header and deleted.
    private static bool IsPartHeader(string text) =>
        SecHeadingKeyword.MatchesKeywordIdentifier(text, "PART", SecHeadingKeyword.IsRomanNumeral);

    private static INode FindFirstMeaningfulSibling(INode node, bool forward)
    {
        var current = forward ? node.NextSibling : node.PreviousSibling;
        while (current != null)
        {
            if (
                current.NodeType == NodeType.Comment
                || string.IsNullOrWhiteSpace(current.TextContent)
            )
            {
                current = forward ? current.NextSibling : current.PreviousSibling;
                continue;
            }
            return current;
        }
        return null;
    }
}
