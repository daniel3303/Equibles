using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal class PaginationRemovalStep : IHtmlNormalizationStep {
    public void Execute(IHtmlDocument doc) {
        var hrElements = doc.Body?.QuerySelectorAll(":scope > hr")?.ToList();
        if (hrElements == null || hrElements.Count == 0) return;

        foreach (var hr in hrElements) {
            var elementsToRemove = new List<INode> { hr };

            // Check element before HR for page number
            var previousSibling = hr.PreviousSibling;
            while (previousSibling != null) {
                if (previousSibling.NodeType == NodeType.Comment ||
                    string.IsNullOrWhiteSpace(previousSibling.TextContent)) {
                    previousSibling = previousSibling.PreviousSibling;
                    continue;
                }

                var textContent = previousSibling.TextContent.Trim();
                if (int.TryParse(textContent, out _)) {
                    elementsToRemove.Add(previousSibling);
                }

                break;
            }

            // Check element after HR for Part header
            var nextSibling = hr.NextSibling;
            while (nextSibling != null) {
                if (nextSibling.NodeType == NodeType.Comment || string.IsNullOrWhiteSpace(nextSibling.TextContent)) {
                    nextSibling = nextSibling.NextSibling;
                    continue;
                }

                var textContent = nextSibling.TextContent.Trim();
                if (textContent.StartsWith("Part", StringComparison.OrdinalIgnoreCase)) {
                    elementsToRemove.Add(nextSibling);
                }

                break;
            }

            foreach (var element in elementsToRemove) {
                element.Parent?.RemoveChild(element);
            }
        }
    }
}
