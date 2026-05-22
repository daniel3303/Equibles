using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

/// <summary>
/// Sibling to Execute_RemovesEmptyParentDivOfIxHeader. That pin establishes
/// the "remove the wrapping div if it became empty after ix:header was
/// stripped" contract. The complementary contract — that a wrapping div with
/// human-readable content NEXT TO the ix:header (such as a signature/logo
/// image) must survive the cleanup — is unpinned. The emptiness check uses
/// `IsNullOrWhiteSpace(parent.TextContent)`; a div whose remaining child is
/// an &lt;img&gt; has an empty TextContent and is incorrectly classified as
/// empty, taking the image with it.
/// </summary>
public class XbrlStripStepNonTextSiblingOfIxHeaderTests
{
    [Fact(
        Skip = "GH-1782 — XbrlStripStep removes the wrapping div when its TextContent is whitespace, dropping non-text siblings of ix:header (img, svg, etc.) along with it"
    )]
    public void Execute_DivContainingIxHeaderAndImage_PreservesImage()
    {
        // The wrapping div carries both an ix:header (to be stripped) and an
        // <img> alongside it. After ix:header removal, the div is visually
        // non-empty (the image is human-readable content), so the parent-
        // removal branch must not fire. The contract derived from the step's
        // purpose — "preserve human-readable content" — applies here.
        var parser = new HtmlParser();
        var step = new XbrlStripStep();
        var doc = parser.ParseDocument(
            "<html><body><div>"
                + "<ix:header><ix:resources></ix:resources></ix:header>"
                + "<img src=\"logo.png\" alt=\"logo\">"
                + "</div></body></html>"
        );

        step.Execute(doc);

        doc.QuerySelectorAll("img")
            .Length.Should()
            .Be(
                1,
                "an <img> alongside the ix:header is visible content; the div must not be removed for having empty TextContent"
            );
    }
}
