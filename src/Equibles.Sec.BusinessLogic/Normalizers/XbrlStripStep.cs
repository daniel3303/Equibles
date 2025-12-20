using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class XbrlStripStep : IHtmlNormalizationStep {
    public void Execute(IHtmlDocument doc) {
        // Step 1: Remove ix:header entirely (contains ix:hidden, ix:references, ix:resources with all xbrli:context/unit)
        var ixHeaders = doc.QuerySelectorAll("*").Where(e => e.LocalName == "ix:header").ToList();
        foreach (var header in ixHeaders) {
            var parent = header.ParentElement;
            header.Remove();

            if (parent != null && parent.LocalName == "div" &&
                string.IsNullOrWhiteSpace(parent.TextContent)) {
                parent.Remove();
            }
        }

        // Step 2: Remove non-ix: namespaced elements (dei:, ebs:, xbrli:, etc.) — never contain human-readable content
        var allElements = doc.QuerySelectorAll("*").ToList();
        foreach (var element in allElements) {
            if (!element.LocalName.Contains(':')) continue;
            if (element.LocalName.StartsWith("ix:")) continue;

            element.Remove();
        }

        // Step 3: Unwrap remaining ix: elements — preserve visible inline content (numbers, text, paragraphs)
        var ixNodes = doc.QuerySelectorAll("*")
            .Where(e => e.LocalName.StartsWith("ix:"))
            .ToList();

        foreach (var node in ixNodes) {
            if (node.ParentElement == null) continue;

            foreach (var child in node.ChildNodes.ToList()) {
                node.ParentElement.InsertBefore(child, node);
            }

            node.Remove();
        }
    }
}
