using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class NameAnchorRemovalStepTests
{
    private readonly HtmlParser _parser = new();

    [Fact]
    public void Execute_LinkTargets_AreUnwrappedAndLinksKept()
    {
        var doc = _parser.ParseDocument(
            "<html><body><p><a name=\"INCOME\"></a><a id=\"TOC2\">Statements</a> of income, "
                + "see <a href=\"#INCOME\">Table of Contents</a></p></body></html>"
        );

        new NameAnchorRemovalStep().Execute(doc);

        doc.Body!.InnerHtml.Should()
            .Be("<p>Statements of income, see <a href=\"#INCOME\">Table of Contents</a></p>");
    }
}
