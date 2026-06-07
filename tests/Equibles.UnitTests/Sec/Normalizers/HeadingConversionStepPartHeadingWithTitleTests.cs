using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepPartHeadingWithTitleTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: a real SEC "Part" heading is promoted to h1 even when the bare "PART <roman>"
    // carries a short standardized title ("PART II — OTHER INFORMATION"). The length guard that
    // rejects long prose cross-references (GH-3512) must not demote these titled headers — only
    // full sentences are excluded, never a brief Part title.
    [Fact]
    public void Execute_PartHeadingFollowedByShortTitle_PromotesItToH1()
    {
        var doc = _parser.ParseDocument(
            "<html><body><div><span>Part II — Other Information</span></div></body></html>"
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().Contain("<h1>");
        body.Should().Contain("Part II — Other Information");
        body.Should().NotContain("<span");
    }
}
