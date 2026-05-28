using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsTabInSchemeTests
{
    // Adversarial Lane A — extends the sibling javascript:/data:/vbscript:/
    // entity-encoded scheme pins. The IsSafeUrl regex matches a scheme via
    // ^\s*([a-zA-Z][a-zA-Z0-9+.\-]*):  — `\s` strips ONLY leading whitespace,
    // it does not tolerate whitespace BETWEEN scheme letters. But per the
    // WHATWG URL spec (and every modern browser) ASCII tab (U+0009), LF
    // (U+000A), and CR (U+000D) are stripped from URL input before parsing —
    // so `java\tscript:alert(1)` is interpreted as `javascript:alert(1)` by
    // the browser, exactly the scheme the existing pins reject.
    //
    // The check's documented purpose is stored-XSS prevention against link
    // destinations (the helper is fed ingested filing/transcript text, which
    // is rendered via Raw()). The sibling pins prove the contract is
    // "must not emit an active javascript: href under any scheme-encoding
    // bypass"; a literal tab inside the scheme is the same bypass class as
    // the entity-encoded colon already pinned in EntityEncodedSchemeTests.
    //
    // Test through the public MarkdownToHtml entry point so the assertion
    // covers the end-to-end attack vector (markdown input → captured HTML).
    [Fact]
    public void MarkdownToHtml_TabInsideJavascriptScheme_DoesNotEmitActiveJavascriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[x](<java\tscript:alert(1)>)");

        captured.Should().NotBeNull();
        captured
            .Should()
            .NotContain(
                "javascript",
                "browsers strip ASCII tab from URL input before scheme parsing, so any rendered href containing the tab-spliced 'java<TAB>script:' sequence resolves to an active javascript: scheme — same XSS class as the entity-encoded colon and bare javascript: pins"
            );
    }
}
