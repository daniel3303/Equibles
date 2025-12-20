using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class ListConversionStep : IHtmlNormalizationStep {
    public void Execute(IHtmlDocument doc) {
        var itemListElements = doc.QuerySelectorAll("div.item-list-element-wrapper").ToList();
        if (itemListElements.Count == 0) return;

        var processedElements = new HashSet<IElement>();

        foreach (var element in itemListElements) {
            if (processedElements.Contains(element)) continue;

            var consecutiveItems = GetConsecutiveItemListElements(element);
            if (consecutiveItems.Count > 0) {
                ConvertToUnorderedList(consecutiveItems, doc);
                processedElements.UnionWith(consecutiveItems);
            }
        }
    }

    private List<IElement> GetConsecutiveItemListElements(IElement startElement) {
        var consecutiveItems = new List<IElement> { startElement };
        var current = startElement.NextSibling;

        while (current != null) {
            if (current.NodeType == NodeType.Text && string.IsNullOrWhiteSpace(current.TextContent)) {
                current = current.NextSibling;
                continue;
            }

            if (current.NodeType == NodeType.Element &&
                current is IElement el &&
                el.LocalName == "div" &&
                (el.GetAttribute("class") ?? "").Contains("item-list-element-wrapper")) {
                consecutiveItems.Add(el);
                current = current.NextSibling;
            } else {
                break;
            }
        }

        return consecutiveItems;
    }

    private void ConvertToUnorderedList(List<IElement> itemElements, IHtmlDocument doc) {
        if (itemElements.Count == 0) return;

        var firstElement = itemElements[0];
        var parentNode = firstElement.ParentElement;
        if (parentNode == null) return;

        var ulElement = doc.CreateElement("ul");

        foreach (var itemElement in itemElements) {
            var liElement = ConvertItemToListItem(itemElement, doc);
            ulElement.AppendChild(liElement);
        }

        parentNode.ReplaceChild(ulElement, firstElement);

        for (int i = 1; i < itemElements.Count; i++) {
            itemElements[i].Remove();
        }
    }

    private IElement ConvertItemToListItem(IElement itemElement, IHtmlDocument doc) {
        var liElement = doc.CreateElement("li");

        // Find and remove the bullet point span
        var bulletSpan = itemElement.QuerySelectorAll("span")
            .FirstOrDefault(s => s.TextContent.Trim() is "•" or "·" or "-");
        bulletSpan?.Remove();

        // Get the content div or use the entire content
        var contentDiv = itemElement.QuerySelectorAll("div")
            .FirstOrDefault(d => (d.GetAttribute("style") ?? "").Contains("display:inline"));
        if (contentDiv != null) {
            liElement.InnerHtml = contentDiv.InnerHtml;
        } else {
            // Fallback: use all content except bullet spans
            var allSpans = itemElement.QuerySelectorAll("span")
                .Where(s => s.TextContent.Trim() is not ("•" or "·" or "-"))
                .ToList();
            foreach (var span in allSpans) {
                liElement.AppendChild(span.Clone(true));
            }
        }

        return liElement;
    }
}
