using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class HeadingConversionStep : IHtmlNormalizationStep
{
    public void Execute(IHtmlDocument doc)
    {
        // Select spans that are NOT descendants of table elements
        var spans = doc.QuerySelectorAll("span").Where(s => s.Closest("table") == null).ToList();
        if (spans.Count == 0)
            return;

        var processedParents = new HashSet<IElement>();

        foreach (var span in spans)
        {
            var spanText = span.TextContent.Trim();
            if (string.IsNullOrEmpty(spanText))
                continue;

            var parent = span.ParentElement;
            if (parent == null || processedParents.Contains(parent))
                continue;

            var siblingSpans = parent.QuerySelectorAll("span").ToList();
            if (siblingSpans.Count == 0)
                continue;

            var combinedText = string.Join(" ", siblingSpans.Select(s => s.TextContent.Trim()))
                .Trim();
            if (string.IsNullOrEmpty(combinedText))
                continue;

            var headingTag = ClassifyHeadingTag(combinedText, siblingSpans);
            if (headingTag == null)
                continue;

            ReplaceNodeWithHeading(parent, headingTag, doc);
            processedParents.Add(parent);
        }
    }

    private string ClassifyHeadingTag(string combinedText, List<IElement> siblingSpans)
    {
        if (
            IsPartHeading(combinedText)
            && AllSiblingsMatch(siblingSpans, s => IsPartHeading(s.TextContent))
        )
            return "h1";
        if (
            IsItemHeading(combinedText)
            && AllSiblingsMatch(siblingSpans, s => IsItemHeading(s.TextContent))
        )
            return "h2";
        if (
            AllSiblingsMatch(
                siblingSpans,
                s => !IsApart(s) && (IsBoldSpan(s) || IsAllUppercase(s) || IsCenterAligned(s))
            )
        )
            return "h3";
        if (AllSiblingsMatch(siblingSpans, s => IsItalicSpan(s) && !IsApart(s)))
            return "h4";
        return null;
    }

    private bool AllSiblingsMatch(List<IElement> spans, Func<IElement, bool> condition)
    {
        if (spans.Count == 0)
            return false;

        var meaningfulSpans = spans
            .Where(s =>
            {
                var text = s.TextContent.Trim();
                return !string.IsNullOrWhiteSpace(text)
                    && text.Length > 2
                    && text.Any(char.IsLetterOrDigit);
            })
            .ToList();

        if (meaningfulSpans.Count == 0)
            return false;

        return meaningfulSpans.All(condition);
    }

    private bool IsCenterAligned(IElement node)
    {
        var style = node.GetAttribute("style") ?? "";
        var parentStyle = node.ParentElement?.GetAttribute("style") ?? "";

        return ContainsCssDeclaration(style, "text-align", "center")
            || ContainsCssDeclaration(parentStyle, "text-align", "center");
    }

    private bool IsApart(IElement node)
    {
        return node.TextContent.Trim().StartsWith("(") && node.TextContent.Trim().EndsWith(")");
    }

    private bool IsAllUppercase(IElement node)
    {
        if (string.IsNullOrWhiteSpace(node.TextContent))
            return false;

        var letters = node.TextContent.Trim().Where(char.IsLetter).ToList();
        return letters.Count > 0 && letters.All(char.IsUpper);
    }

    private bool IsPartHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var upperText = text.ToUpperInvariant().Trim();

        // SEC EDGAR renders "Part N" with a non-breaking space (U+00A0)
        // between word and numeral, so accept any Unicode whitespace after
        // "PART" — not just the literal U+0020 that StartsWith("PART ") demands.
        // Mirrors the GH-975 fix applied to IsItemHeading.
        if (upperText.StartsWith("PART") && upperText.Length > 4 && char.IsWhiteSpace(upperText[4]))
        {
            var afterPart = upperText.Substring(5).Trim();
            if (!string.IsNullOrEmpty(afterPart))
            {
                var firstWord = afterPart.Split(
                    [' ', '.', '-', ':'],
                    StringSplitOptions.RemoveEmptyEntries
                )[0];
                return !string.IsNullOrEmpty(firstWord) && firstWord.All(char.IsLetter);
            }
        }

        return false;
    }

    private bool IsItemHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var upperText = text.ToUpperInvariant().Trim();

        // SEC EDGAR renders "Item N" with a non-breaking space (U+00A0)
        // between word and number, so accept any Unicode whitespace after
        // "ITEM" — not just the literal U+0020 that StartsWith("ITEM ") demands.
        return upperText.StartsWith("ITEM")
            && upperText.Length > 5
            && char.IsWhiteSpace(upperText[4]);
    }

    private bool IsItalicSpan(IElement span) => HasInlineCss(span, "font-style", "italic");

    private void ReplaceNodeWithHeading(IElement node, string headingTag, IHtmlDocument doc)
    {
        var heading = doc.CreateElement(headingTag);
        heading.TextContent = node.TextContent.Trim();
        node.ParentElement?.ReplaceChild(heading, node);
    }

    private bool IsBoldSpan(IElement span) => HasInlineCss(span, "font-weight", "bold");

    // SEC EDGAR sometimes leaves the styling on the span itself (via the
    // `style` attribute) and sometimes on a child element (rendered into
    // `innerHtml`), so both surfaces have to be inspected.
    private static bool HasInlineCss(IElement span, string property, string value)
    {
        var style = span.GetAttribute("style") ?? "";
        return ContainsCssDeclaration(style, property, value)
            || ContainsCssDeclaration(span.InnerHtml, property, value);
    }

    // SEC EDGAR emits inline CSS with and without a space after the colon
    // (e.g. "font-weight:bold" and "font-weight: bold"); both forms must match.
    private static bool ContainsCssDeclaration(string source, string property, string value)
    {
        return source.Contains($"{property}:{value}") || source.Contains($"{property}: {value}");
    }
}
