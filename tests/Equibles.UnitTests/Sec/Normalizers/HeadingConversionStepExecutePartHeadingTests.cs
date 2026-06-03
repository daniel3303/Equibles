using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepExecutePartHeadingTests
{
    private readonly HeadingConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    [Fact]
    public void Execute_BlockWhoseSpanIsAPartHeading_PromotesItToH1()
    {
        // Contract: the pipeline classifies an SEC "Part N" heading at the TOP tier (h1) — above
        // the Item (h2) tier covered separately. This is the distinct Part→h1 classification branch
        // of Execute; a regression mis-tiering Part as h2/h3 would break the document outline.
        var doc = _parser.ParseDocument("<html><body><div><span>Part I</span></div></body></html>");

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().Contain("<h1>");
        body.Should().Contain("Part I");
        body.Should().NotContain("<span");
    }
}
