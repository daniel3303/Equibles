using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class ListConversionStepSpacedInlineStyleTests
{
    private readonly ListConversionStep _step = new();
    private readonly HtmlParser _parser = new();

    // SEC EDGAR emits inline CSS both as "display:inline" and "display: inline" (the
    // sibling HeadingConversionStep documents this and matches both). The contract:
    // the inline-display content div is unwrapped INTO the <li> (per the no-space pin
    // ContentFromInlineDisplayDiv_IsPreservedInLi). A spaced "display: inline" must be
    // treated identically — the wrapper div must not survive inside the list item.
    [Fact(Skip = "GH-3435 — spaced \"display: inline\" content div is not unwrapped into the <li>")]
    public void Execute_ContentDivWithSpacedDisplayInline_UnwrapsContentIntoLi()
    {
        var doc = _parser.ParseDocument(
            """
            <html><body>
            <div class="item-list-element-wrapper">
              <span>-</span>
              <div style="display: inline">Preserved <strong>bold</strong> content</div>
            </div>
            </body></html>
            """
        );

        _step.Execute(doc);

        var body = doc.Body!.InnerHtml;
        body.Should().Contain("Preserved <strong>bold</strong> content");
        // The inline-display wrapper must be lifted out, exactly as the no-space form is.
        body.Should().NotContain("display:");
    }
}
