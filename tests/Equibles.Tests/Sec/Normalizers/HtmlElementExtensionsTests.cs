using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.Tests.Sec.Normalizers;

public class HtmlElementExtensionsTests {
    [Fact]
    public void InsertAfter_RefNodeIsLastChild_AppendsAtEnd() {
        var doc = new HtmlParser().ParseDocument("<html><body><div id=\"p\"><span id=\"first\"></span></div></body></html>");
        var parent = doc.GetElementById("p")!;
        var refNode = doc.GetElementById("first")!;
        var newNode = doc.CreateElement("em");
        newNode.SetAttribute("id", "added");

        HtmlElementExtensions.InsertAfter(parent, newNode, refNode);

        parent.LastElementChild!.GetAttribute("id").Should().Be("added");
    }
}
