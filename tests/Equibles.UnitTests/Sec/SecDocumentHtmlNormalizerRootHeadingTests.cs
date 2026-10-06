using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SecDocumentHtmlNormalizerRootHeadingTests
{
    [Theory]
    [InlineData("<span>ACCRUED EXPENSES AND PAYABLES</span>")]
    [InlineData("<span style=\"font-weight:bold\">Note title</span>")]
    [InlineData("<span style=\"font-style:italic\">Note title</span>")]
    [InlineData("<span>PART II</span>")]
    [InlineData("<span>Item 7. Borrowings</span>")]
    public void NormalizeFragment_RootHeading_PreservesFollowingParagraphAndTable(string heading)
    {
        var markup =
            heading
            + "<p>Amounts are reported in thousands.</p>"
            + "<table><tr><td>2026</td><td>2025</td></tr>"
            + "<tr><td>7,355</td><td>6,263</td></tr></table>";

        var normalized = new SecDocumentHtmlNormalizer().NormalizeFragment(markup);
        var document = new HtmlParser().ParseDocument(normalized);

        document.Body.TextContent.Should().Contain("Amounts are reported in thousands.");
        document.QuerySelectorAll("table").Should().HaveCount(1);
        document.QuerySelectorAll("tr").Should().HaveCount(2);
        var amounts = document.QuerySelectorAll("tr")[1].TextContent;
        amounts.Should().Contain("7,355").And.Contain("6,263");
        document.QuerySelectorAll("h1,h2,h3,h4").Should().BeEmpty();
    }

    [Fact]
    public void NormalizeFragment_StandaloneRootHeading_RemainsReadable()
    {
        var result = new SecDocumentHtmlNormalizer().NormalizeFragment("<span>PAYABLES</span>");

        result.Should().Be("<span>PAYABLES</span>");
    }

    [Fact]
    public void NormalizeFragment_HeadingInsideParagraph_StillBecomesHeading()
    {
        var result = new SecDocumentHtmlNormalizer().NormalizeFragment(
            "<p><span>PAYABLES</span></p><p>Amounts are reported in thousands.</p>"
        );

        result.Should().Contain("<h3>PAYABLES</h3>");
        result.Should().Contain("<p>Amounts are reported in thousands.</p>");
    }
}
