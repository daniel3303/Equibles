using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepSiblingClassTokenTests
{
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // Contract: only "item-list-element-wrapper" divs form a list. The run starts from a
    // CSS class-TOKEN match (div.item-list-element-wrapper), so a sibling whose class only
    // contains that text as part of a larger token (e.g. "sub-item-list-element-wrapper")
    // is a different element and must NOT be absorbed into the list. Oracle from the
    // contract: exactly one <li> (the real item); the unrelated sibling stays outside.
    [Fact]
    public void Execute_SiblingClassContainsWrapperAsSubstringOnly_IsNotMergedIntoList()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
            <div class="item-list-element-wrapper">
              <span>•</span>
              <div style="display:inline">Real item</div>
            </div>
            <div class="sub-item-list-element-wrapper">Unrelated sibling block</div>
            </body></html>
            """
        );

        _step.Execute(doc);

        var liCount = doc.Body!.QuerySelectorAll("li").Length;
        liCount.Should().Be(1);
        // The unrelated sibling div must survive as itself, not be lifted into the list.
        doc.Body!.QuerySelector("div.sub-item-list-element-wrapper").Should().NotBeNull();
    }
}
