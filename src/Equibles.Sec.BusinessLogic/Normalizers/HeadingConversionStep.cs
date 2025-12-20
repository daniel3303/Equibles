using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class HeadingConversionStep : IHtmlNormalizationStep {
    public void Execute(IHtmlDocument doc) {
        // Select spans that are NOT descendants of table elements
        var spans = doc.QuerySelectorAll("span")
            .Where(s => s.Closest("table") == null)
            .ToList();
        if (spans.Count == 0) return;

        var processedParents = new HashSet<IElement>();

        foreach (var span in spans) {
            var spanText = span.TextContent.Trim();
            if (string.IsNullOrEmpty(spanText)) continue;

            var parent = span.ParentElement;
            if (parent == null || processedParents.Contains(parent)) continue;

            var siblingSpans = parent.QuerySelectorAll("span").ToList();
            if (siblingSpans.Count == 0) continue;

            var combinedText = string.Join(" ", siblingSpans.Select(s => s.TextContent.Trim())).Trim();
            if (string.IsNullOrEmpty(combinedText)) continue;

            if (IsPartHeading(combinedText) && AllSiblingsMatch(siblingSpans, s => IsPartHeading(s.TextContent))) {
                ReplaceNodeWithHeading(parent, "h1", doc);
                processedParents.Add(parent);
            } else if (IsItemHeading(combinedText) && AllSiblingsMatch(siblingSpans, s => IsItemHeading(s.TextContent))) {
                ReplaceNodeWithHeading(parent, "h2", doc);
                processedParents.Add(parent);
            } else if (AllSiblingsMatch(siblingSpans,
                           s => !IsApart(s) && (IsBoldSpan(s) || IsAllUppercase(s) || IsCenterAligned(s)))) {
                ReplaceNodeWithHeading(parent, "h3", doc);
                processedParents.Add(parent);
            } else if (AllSiblingsMatch(siblingSpans, s => IsItalicSpan(s) && !IsApart(s))) {
                ReplaceNodeWithHeading(parent, "h4", doc);
                processedParents.Add(parent);
            }
        }
    }

    private bool AllSiblingsMatch(List<IElement> spans, Func<IElement, bool> condition) {
        if (spans.Count == 0) return false;

        var meaningfulSpans = spans.Where(s => {
            var text = s.TextContent.Trim();
            return !string.IsNullOrWhiteSpace(text) &&
                   text.Length > 2 &&
                   text.Any(char.IsLetterOrDigit);
        }).ToList();

        if (meaningfulSpans.Count == 0) return false;

        return meaningfulSpans.All(condition);
    }

    private bool IsCenterAligned(IElement node) {
        var style = node.GetAttribute("style") ?? "";
        var parentStyle = node.ParentElement?.GetAttribute("style") ?? "";

        return style.Contains("text-align:center") ||
               style.Contains("text-align: center") ||
               parentStyle.Contains("text-align:center") ||
               parentStyle.Contains("text-align: center");
    }

    private bool IsApart(IElement node) {
        return node.TextContent.Trim().StartsWith("(") && node.TextContent.Trim().EndsWith(")");
    }

    private bool IsAllUppercase(IElement node) {
        if (string.IsNullOrWhiteSpace(node.TextContent)) return false;

        return node.TextContent.Trim().Where(char.IsLetter).All(char.IsUpper);
    }

    private bool IsPartHeading(string text) {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var upperText = text.ToUpperInvariant().Trim();

        if (upperText.StartsWith("PART ")) {
            var afterPart = upperText.Substring(5).Trim();
            if (!string.IsNullOrEmpty(afterPart)) {
                var firstWord = afterPart.Split([' ', '.', '-', ':'], StringSplitOptions.RemoveEmptyEntries)[0];
                return !string.IsNullOrEmpty(firstWord) && firstWord.All(char.IsLetter);
            }
        }

        return false;
    }

    private bool IsItemHeading(string text) {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var upperText = text.ToUpperInvariant().Trim();

        return upperText.StartsWith("ITEM ") && upperText.Length > 5;
    }

    private bool IsItalicSpan(IElement span) {
        var style = span.GetAttribute("style") ?? "";
        var innerHtml = span.InnerHtml;

        return style.Contains("font-style:italic") ||
               style.Contains("font-style: italic") ||
               innerHtml.Contains("font-style:italic") ||
               innerHtml.Contains("font-style: italic");
    }

    private void ReplaceNodeWithHeading(IElement node, string headingTag, IHtmlDocument doc) {
        var heading = doc.CreateElement(headingTag);
        heading.TextContent = node.TextContent.Trim();
        node.ParentElement?.ReplaceChild(heading, node);
    }

    private bool IsBoldSpan(IElement span) {
        var style = span.GetAttribute("style") ?? "";
        var innerHtml = span.InnerHtml;

        return style.Contains("font-weight:bold") ||
               style.Contains("font-weight: bold") ||
               innerHtml.Contains("font-weight:bold") ||
               innerHtml.Contains("font-weight: bold");
    }
}
