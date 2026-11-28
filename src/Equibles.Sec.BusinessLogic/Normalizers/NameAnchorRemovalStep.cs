using AngleSharp.Html.Dom;

namespace Equibles.Sec.BusinessLogic.Normalizers;

// A link target (<a name> or <a id> without href) converts to an empty Markdown link "[]()" that
// Markdown cannot point at anyway, so it is unwrapped and its content kept.
internal class NameAnchorRemovalStep : IHtmlNormalizationStep
{
    public void Execute(IHtmlDocument doc)
    {
        foreach (var anchor in doc.QuerySelectorAll("a:not([href])").ToList())
        {
            if (anchor.ChildNodes.Length == 0)
                anchor.Remove();
            else
                anchor.Replace([.. anchor.ChildNodes]);
        }
    }
}
