using AngleSharp.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

internal static class HtmlElementExtensions {
    internal static List<IElement> DirectChildCells(IElement row) {
        return row.Children.Where(c => c.LocalName is "td" or "th").ToList();
    }

    internal static void InsertAfter(INode parent, INode newNode, INode refNode) {
        parent.InsertBefore(newNode, refNode.NextSibling);
    }
}
