using System.Text;
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

    // A block deeper than this, or holding more spans, is never judged a heading. In 45 real
    // filings and European reports the deepest block judged sat 14 elements deep, and the largest
    // heading held 52 spans.
    private const int MaxJudgedDepth = 64;
    private const int MaxJudgedSpans = 256;

    private static readonly System.Buffers.SearchValues<char> WhiteSpace =
        System.Buffers.SearchValues.Create(
            Enumerable
                .Range(0, char.MaxValue + 1)
                .Select(c => (char)c)
                .Where(char.IsWhiteSpace)
                .ToArray()
        );

    // Whether an element's inner markup holds a declaration, per element, for the length of one
    // Execute: a block of nested spans asks it of each span inside the one before.
    [ThreadStatic]
    private static Dictionary<(IElement Element, string Text), bool> _markup;

    public void Execute(IHtmlDocument doc)
    {
        _markup = [];
        try
        {
            Run(doc);
        }
        finally
        {
            _markup = null;
        }
    }

    private void Run(IHtmlDocument doc)
    {
        // Select spans that are NOT descendants of table elements
        var places = new Dictionary<IElement, (int Depth, bool InTable)>();
        var spans = doc.QuerySelectorAll("span").Where(s => !Place(s, places).InTable).ToList();
        if (spans.Count == 0)
            return;

        var processedParents = new HashSet<IElement>();
        // A parent that is no heading stays none until something under it changes, so each is judged
        // once and not once per span it holds: a converted report puts every word of a page in its
        // own span, and judging the page again for each took time and memory with the square of them.
        var notHeadings = new HashSet<IElement>();
        // Spans a heading replaced are out of the document: nothing done under them shows.
        var replaced = new HashSet<IElement>();

        foreach (var span in spans)
        {
            var parent = span.ParentElement;
            // Judging a block reads what each of its spans holds, so a chain of spans each holding
            // the rest is read once per link. The two bounds cap the blocks judged and the spans
            // read in each, which keeps the step linear in the document whatever its shape.
            if (
                parent == null
                || Place(parent, places).Depth > MaxJudgedDepth
                || replaced.Contains(span)
                || !HasText(span)
            )
                continue;
            if (processedParents.Contains(parent) || notHeadings.Contains(parent))
                continue;
            // A root-level note heading cannot replace the document container or its other content.
            if (ReferenceEquals(parent, doc.Body) || ReferenceEquals(parent, doc.DocumentElement))
                continue;

            // Read lazily, in document order as the selector lists them: each check stops at the
            // first span that settles it, so a block of nested spans is not walked through each time.
            var siblingSpans = Spans(parent);
            if (siblingSpans.Skip(MaxJudgedSpans).Any())
            {
                notHeadings.Add(parent);
                continue;
            }

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
            {
                notHeadings.Remove(ancestor);
                foreach (var declaration in Declarations)
                    _markup.Remove((ancestor, declaration));
            }
            ReplaceNodeWithHeading(parent, headingTag, doc);
            processedParents.Add(parent);
            replaced.UnionWith(siblingSpans);
        }
    }

    // How many elements hold an element, and whether a table is among them (Closest("table")),
    // each element's answer kept so a deep block is climbed once.
    private static (int Depth, bool InTable) Place(
        IElement element,
        Dictionary<IElement, (int Depth, bool InTable)> known
    )
    {
        var climbed = new List<IElement>();
        (int Depth, bool InTable) above = (0, false);
        for (var current = element; current != null; current = current.ParentElement)
        {
            if (known.TryGetValue(current, out above))
                break;
            climbed.Add(current);
        }
        for (var index = climbed.Count - 1; index >= 0; index--)
        {
            above = (above.Depth + 1, above.InTable || IsNamed(climbed[index], "table"));
            known[climbed[index]] = above;
        }
        return known[element];
    }

    private static bool IsNamed(IElement element, string name) =>
        element.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<IElement> Spans(IElement parent) =>
        parent.GetDescendants().OfType<IElement>().Where(element => IsNamed(element, "span"));

    // Whether the span's text holds anything but white space, read only as far as the first such
    // character: a span nested in others holds all their text.
    private static bool HasText(IElement span) =>
        span.GetDescendants()
            .OfType<IText>()
            .Any(text => text.Data.AsSpan().ContainsAnyExcept(WhiteSpace));

    // The spans' text as one line, or no line once it runs past the words a Part or Item heading
    // may have: only those two rules read it, and spans nested in one another repeat their text
    // at every level, which a line of all of them would hold many times over.
    private static (bool HasText, string Text) CombineText(IEnumerable<IElement> spans)
    {
        var limit = Math.Max(MaxPartHeadingWords, MaxItemHeadingWords);
        var words = 0;
        foreach (var span in spans)
        {
            words += Words(span, limit - words + 1);
            if (words > limit)
                return (true, null);
        }
        var combined = string.Join(" ", spans.Select(span => span.TextContent.Trim())).Trim();
        return (combined.Length > 0, combined);
    }

    // The words of the span's text as SecHeadingKeyword.WordCount counts them, counted only up to
    // the limit, so a span holding a whole page is not read through.
    private static int Words(IElement span, int limit)
    {
        var words = 0;
        var inWord = false;
        foreach (var text in span.GetDescendants().OfType<IText>())
        {
            foreach (var character in text.Data)
            {
                if (char.IsWhiteSpace(character))
                    inWord = false;
                else if (!inWord)
                {
                    inWord = true;
                    if (++words >= limit)
                        return words;
                }
            }
        }
        return words;
    }

    private string ClassifyHeadingTag(string combinedText, IEnumerable<IElement> siblingSpans)
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

    private bool AllSiblingsMatch(IEnumerable<IElement> spans, Func<IElement, bool> condition)
    {
        // The first span that fails the condition settles it, before the text of the rest is read.
        var anyMeaningful = false;
        foreach (var span in spans)
        {
            if (!IsMeaningful(span))
                continue;
            anyMeaningful = true;
            if (!condition(span))
                return false;
        }
        return anyMeaningful;
    }

    // Whether the span's trimmed text is longer than two characters and holds a letter or digit,
    // read only as far as it takes to know.
    private static bool IsMeaningful(IElement span)
    {
        var characters = 0;
        var spaceBetween = false;
        var pendingSpace = false;
        var letterOrDigit = false;
        foreach (var text in span.GetDescendants().OfType<IText>())
        {
            foreach (var character in text.Data)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = characters > 0;
                    continue;
                }
                spaceBetween |= pendingSpace;
                pendingSpace = false;
                characters++;
                letterOrDigit |= char.IsLetterOrDigit(character);
                if (characters >= 3 && letterOrDigit)
                    return true;
            }
        }
        // Two characters reach three with a space between them.
        return letterOrDigit && (characters >= 3 || (characters == 2 && spaceBetween));
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
        // A text opening with a letter or digit cannot start with "(" under any comparison, and it is
        // known from the first character; anything else takes the full comparison.
        var first = FirstNonWhiteSpace(node);
        if (first is null || char.IsLetterOrDigit(first.Value))
            return false;
        return Trimmed(node).StartsWith("(") && Trimmed(node).EndsWith(")");
    }

    private static string Trimmed(IElement node) => node.TextContent.Trim();

    private static char? FirstNonWhiteSpace(IElement node)
    {
        foreach (var text in node.GetDescendants().OfType<IText>())
        {
            foreach (var character in text.Data)
            {
                if (!char.IsWhiteSpace(character))
                    return character;
            }
        }
        return null;
    }

    // Read up to the first lower-case letter, which settles it.
    private bool IsAllUppercase(IElement node)
    {
        var anyLetter = false;
        foreach (var text in node.GetDescendants().OfType<IText>())
        {
            foreach (var character in text.Data)
            {
                if (!char.IsLetter(character))
                    continue;
                if (!char.IsUpper(character))
                    return false;
                anyLetter = true;
            }
        }
        return anyLetter;
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
            || InnerMarkupContains(span, $"{property}:{value}")
            || InnerMarkupContains(span, $"{property}: {value}");
    }

    private static readonly string[] Declarations =
    [
        "font-weight:bold",
        "font-weight: bold",
        "font-style:italic",
        "font-style: italic",
    ];

    // Whether span.InnerHtml holds the text, without writing it out. The text is a declaration's
    // letters, hyphens, colon and space, none of which the writer escapes, and the tags it writes
    // break any run, so it can only lie within one child's tag name, one attribute name or value,
    // one comment, or a run of adjacent text; a child element answers for its own inner markup.
    private static bool InnerMarkupContains(IElement element, string text)
    {
        var known = _markup ?? [];
        if (known.TryGetValue((element, text), out var answer))
            return answer;
        // Children first, without recursion: a block can nest spans thousands deep.
        var pending = new Stack<(IElement Element, bool ChildrenDone)>();
        pending.Push((element, false));
        while (pending.Count > 0)
        {
            var (current, childrenDone) = pending.Pop();
            if (known.ContainsKey((current, text)))
                continue;
            if (!childrenDone)
            {
                pending.Push((current, true));
                foreach (var child in current.Children)
                    if (!known.ContainsKey((child, text)))
                        pending.Push((child, false));
                continue;
            }
            known[(current, text)] = OwnMarkupContains(current, text, known);
        }
        return known[(element, text)];
    }

    private static bool OwnMarkupContains(
        IElement element,
        string text,
        Dictionary<(IElement Element, string Text), bool> known
    )
    {
        var run = new StringBuilder();
        foreach (var node in element.ChildNodes)
        {
            if (node is IText adjacent)
            {
                run.Append(adjacent.Data);
                continue;
            }
            if (Holds(run, text))
                return true;
            run.Clear();
            if (node is IComment comment && comment.Data.Contains(text, StringComparison.Ordinal))
                return true;
            if (node is not IElement child)
                continue;
            if (
                child.LocalName.Contains(text, StringComparison.Ordinal)
                || (child.Prefix + ":" + child.LocalName).Contains(text, StringComparison.Ordinal)
                || child.Attributes.Any(attribute =>
                    attribute.Name.Contains(text, StringComparison.Ordinal)
                    || attribute.Value.Contains(text, StringComparison.Ordinal)
                )
                || known[(child, text)]
            )
                return true;
        }
        return Holds(run, text);
    }

    private static bool Holds(StringBuilder run, string text) =>
        run.Length >= text.Length && run.ToString().Contains(text, StringComparison.Ordinal);
}
