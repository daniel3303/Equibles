using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal sealed class InlineXbrlProseConversionStep : IHtmlNormalizationStep
{
    private const string InlineNamespace = "http://www.xbrl.org/2013/inlineXBRL";
    private const int MaximumChainLength = 128;
    private const int MaximumParagraphCharacters = 8000;

    public void Execute(IHtmlDocument doc)
    {
        var elements = doc.QuerySelectorAll("*").ToArray();
        var roots = elements.Where(element => IsInline(element, "nonnumeric")
            && element.HasAttribute("continuedAt")).ToArray();
        if (roots.Length == 0)
            return;
        var styles = InlineXbrlProseStyles.TryCreate(doc);
        if (styles == null)
            return;
        var order = elements.Select((element, index) => (element, index))
            .ToDictionary(item => item.element, item => item.index);
        var ids = elements.Where(element => !string.IsNullOrEmpty(element.Id))
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var references = elements.Where(element => element.HasAttribute("continuedAt"))
            .GroupBy(element => element.GetAttribute("continuedAt"), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (root.GetAttribute("escape") is not ("true" or "1")
                || string.IsNullOrWhiteSpace(root.GetAttribute("contextRef"))
                || string.IsNullOrWhiteSpace(root.GetAttribute("name"))
                || !SafeAncestors(root))
                continue;
            var chain = ReadChain(root, ids, references, order);
            if (chain == null)
                continue;
            // Work backwards so a continued paragraph can span several linked fragments.
            for (var index = chain.Count - 2; index >= 0; index--)
                JoinBoundary(chain[index], chain[index + 1], doc, styles);
        }
    }

    private static List<IElement> ReadChain(IElement root,
        Dictionary<string, IElement[]> ids, Dictionary<string, int> references,
        Dictionary<IElement, int> order)
    {
        var chain = new List<IElement> { root };
        var visited = new HashSet<IElement> { root };
        while (chain[^1].HasAttribute("continuedAt"))
        {
            var reference = chain[^1].GetAttribute("continuedAt");
            if (chain.Count >= MaximumChainLength || string.IsNullOrWhiteSpace(reference)
                || !ids.TryGetValue(reference, out var matches) || matches.Length != 1
                || references[reference] != 1 || !IsInline(matches[0], "continuation")
                || order[matches[0]] <= order[chain[^1]]
                || !SafeAncestors(matches[0]) || !visited.Add(matches[0])
                || chain.Any(previous => previous.Contains(matches[0]) || matches[0].Contains(previous)))
                return null;
            chain.Add(matches[0]);
        }
        return chain;
    }

    private static void JoinBoundary(IElement source, IElement continuation, IHtmlDocument doc,
        InlineXbrlProseStyles styles)
    {
        if (!FactAncestors(source).SequenceEqual(FactAncestors(continuation)))
            return;
        var before = BoundaryParagraph(source, last: true);
        var after = BoundaryParagraph(continuation, last: false);
        if (before == null || after == null || before == after
            || !styles.SafeBoundary(before) || !styles.SafeBoundary(after))
            return;
        var ending = before.TextContent.TrimEnd();
        var beginning = after.TextContent.TrimStart();
        if (ending.Length == 0 || beginning.Length == 0
            || before.TextContent.Length + after.TextContent.Length > MaximumParagraphCharacters
            || !(char.IsLetter(ending[^1]) || ending[^1] == '-')
            || !char.IsLower(beginning[0]) || !OnlyLayoutBetween(before, after))
            return;
        // Retain the printed break and every character, including hyphenation and figures.
        before.AppendChild(doc.CreateElement("br"));
        foreach (var child in after.ChildNodes.ToArray())
            before.AppendChild(child);
        after.Remove();
    }

    private static IElement BoundaryParagraph(IElement owner, bool last)
    {
        var paragraphs = owner.QuerySelectorAll("p");
        var paragraph = last ? paragraphs.LastOrDefault() : paragraphs.FirstOrDefault();
        if (paragraph == null || !SafeAncestors(paragraph)
            || paragraph.QuerySelector("table,ul,ol,dl,h1,h2,h3,h4,h5,h6,script,style") != null
            || paragraph.QuerySelectorAll("*").Any(element =>
                IsInline(element, "exclude") || IsInline(element, "nonnumeric")
                || IsInline(element, "continuation") || !SafeAncestors(element)))
            return null;
        for (var parent = paragraph.ParentElement; parent != null; parent = parent.ParentElement)
        {
            if (parent == owner)
                return paragraph;
            if (IsInline(parent, "nonnumeric") || IsInline(parent, "continuation"))
                return null;
        }
        return null;
    }

    private static bool OnlyLayoutBetween(IElement before, IElement after)
    {
        for (var node = NextAfter(before); node != null; node = Next(node))
        {
            if (node == after)
                return true;
            if (node is IText text && !string.IsNullOrWhiteSpace(text.Data))
                return false;
            if (node is IElement element
                && !(element.LocalName is "div" or "section" or "article" or "br" or "wbr"
                    || IsInline(element, "continuation") || IsInline(element, "nonnumeric")))
                return false;
        }
        return false;
    }

    private static INode Next(INode node) => node.FirstChild ?? NextAfter(node);

    private static INode NextAfter(INode node)
    {
        for (var current = node; current != null; current = current.Parent)
            if (current.NextSibling != null)
                return current.NextSibling;
        return null;
    }

    private static bool SafeAncestors(IElement element)
    {
        for (var current = element; current != null; current = current.ParentElement)
        {
            if (current.LocalName is "table" or "script" or "style" or "blockquote" or "q"
                or "li" or "ul" or "ol" or "dl" or "dt" or "dd" or "pre" or "del" or "s" or "strike"
                or "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                || current.HasAttribute("hidden") || current.GetAttribute("aria-hidden") == "true"
                || IsInline(current, "header") || IsInline(current, "hidden")
                || IsInline(current, "exclude") || IsInline(current, "footnote"))
                return false;
        }
        return true;
    }

    private static IEnumerable<IElement> FactAncestors(IElement element)
    {
        for (var parent = element.ParentElement; parent != null; parent = parent.ParentElement)
            if (IsInline(parent, "nonnumeric") || IsInline(parent, "continuation"))
                yield return parent;
    }

    private static bool IsInline(IElement element, string name)
    {
        if (element.LocalName != "ix:" + name)
            return false;
        for (var current = element; current != null; current = current.ParentElement)
            if (current.HasAttribute("xmlns:ix"))
                return current.GetAttribute("xmlns:ix") == InlineNamespace;
        return false;
    }
}
