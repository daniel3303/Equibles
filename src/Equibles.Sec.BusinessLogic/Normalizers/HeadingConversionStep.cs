using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class HeadingConversionStep : IHtmlNormalizationStep
{
    // A real SEC "Part" heading is short — "PART II", at most followed by a brief title
    // ("PART II — INFORMATION NOT REQUIRED IN PROSPECTUS" is ~8 words). A prose
    // cross-reference like "Part II of this Annual Report on Form 10-K contains …" opens with
    // the same keyword + roman numeral but runs on much longer, so only a short candidate
    // qualifies as a heading. The threshold clears every standard Part title with margin while
    // rejecting full sentences.
    private const int MaxPartHeadingWords = 12;

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

    // A real Part identifier is a Roman numeral (Part I–IV); prose beginning "Part of …"
    // or a word like "Partnership" must not be tagged as a heading — and neither must a long
    // prose sentence that merely opens "Part <roman> …", so the candidate must also be short.
    private bool IsPartHeading(string text) =>
        SecHeadingKeyword.MatchesKeywordIdentifier(text, "PART", SecHeadingKeyword.IsRomanNumeral)
        && WordCount(text) <= MaxPartHeadingWords;

    // Whitespace-delimited word count; SEC EDGAR's non-breaking space (U+00A0) counts as a
    // separator like any other Unicode whitespace.
    private static int WordCount(string text) =>
        text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

    // A real Item identifier is number-led (Item 1, 1A, 7A); prose beginning "Item of …"
    // starts with a letter and must not be tagged as a heading.
    private bool IsItemHeading(string text) =>
        SecHeadingKeyword.MatchesKeywordIdentifier(
            text,
            "ITEM",
            firstWord => char.IsDigit(firstWord[0])
        );

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
