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

    // A real SEC "Item" heading is short too — "Item 1. Business" through the longest standard
    // title "Item 7A. Quantitative and Qualitative Disclosures About Market Risk" (~9 words). A
    // prose cross-reference like "Item 1 of this Annual Report contains …" opens with the same
    // keyword + digit but runs on much longer, so only a short candidate qualifies as a heading.
    private const int MaxItemHeadingWords = 12;

    public void Execute(IHtmlDocument doc)
    {
        // Select spans that are NOT descendants of table elements
        var spans = doc.QuerySelectorAll("span").Where(s => s.Closest("table") == null).ToList();
        if (spans.Count == 0)
            return;

        var processedParents = new HashSet<IElement>();
        // A parent that is no heading stays none until something under it changes, so each is judged
        // once and not once per span it holds: a converted report puts every word of a page in its
        // own span, and judging the page again for each took time and memory with the square of them.
        var notHeadings = new HashSet<IElement>();

        foreach (var span in spans)
        {
            var spanText = span.TextContent.Trim();
            if (string.IsNullOrEmpty(spanText))
                continue;

            var parent = span.ParentElement;
            if (parent == null || processedParents.Contains(parent) || notHeadings.Contains(parent))
                continue;
            // A root-level note heading cannot replace the document container or its other content.
            if (ReferenceEquals(parent, doc.Body) || ReferenceEquals(parent, doc.DocumentElement))
                continue;

            var siblingSpans = parent.QuerySelectorAll("span").ToList();
            if (siblingSpans.Count == 0)
                continue;

            var (hasText, combinedText) = CombineText(siblingSpans);
            var headingTag = hasText ? ClassifyHeadingTag(combinedText, siblingSpans) : null;
            if (headingTag == null)
            {
                notHeadings.Add(parent);
                continue;
            }

            // The heading takes spans away from every element above it, which may make one of
            // those a heading in turn.
            foreach (var ancestor in parent.GetAncestors().OfType<IElement>())
                notHeadings.Remove(ancestor);
            ReplaceNodeWithHeading(parent, headingTag, doc);
            processedParents.Add(parent);
        }
    }

    // The spans' text as one line, or no line once it runs past the words a Part or Item heading
    // may have: only those two rules read it, and spans nested in one another repeat their text
    // at every level, which a line of all of them would hold many times over.
    private static (bool HasText, string Text) CombineText(List<IElement> spans)
    {
        var texts = new List<string>(spans.Count);
        var words = 0;
        foreach (var span in spans)
        {
            var text = span.TextContent.Trim();
            texts.Add(text);
            words += SecHeadingKeyword.WordCount(text);
            if (words > Math.Max(MaxPartHeadingWords, MaxItemHeadingWords))
                return (true, null);
        }
        var combined = string.Join(" ", texts).Trim();
        return (combined.Length > 0, combined);
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

        // The first span that fails the condition settles it, before the text of the rest is read.
        var anyMeaningful = false;
        foreach (var span in spans)
        {
            var text = span.TextContent.Trim();
            if (
                string.IsNullOrWhiteSpace(text)
                || text.Length <= 2
                || !text.Any(char.IsLetterOrDigit)
            )
                continue;
            anyMeaningful = true;
            if (!condition(span))
                return false;
        }
        return anyMeaningful;
    }

    private bool IsCenterAligned(IElement node)
    {
        var style = node.GetAttribute("style") ?? "";
        var parentStyle = node.ParentElement?.GetAttribute("style") ?? "";

        return SecInlineCss.ContainsDeclaration(style, "text-align", "center")
            || SecInlineCss.ContainsDeclaration(parentStyle, "text-align", "center");
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
        && SecHeadingKeyword.WordCount(text) <= MaxPartHeadingWords;

    // A real Item identifier is number-led (Item 1, 1A, 7A); prose beginning "Item of …"
    // starts with a letter and must not be tagged as a heading — and neither must a long prose
    // sentence that merely opens "Item <n> …", so the candidate must also be short.
    private bool IsItemHeading(string text) =>
        SecHeadingKeyword.MatchesKeywordIdentifier(
            text,
            "ITEM",
            firstWord => char.IsDigit(firstWord[0])
        )
        && SecHeadingKeyword.WordCount(text) <= MaxItemHeadingWords;

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
        return SecInlineCss.ContainsDeclaration(style, property, value)
            || SecInlineCss.ContainsDeclaration(span.InnerHtml, property, value);
    }
}
