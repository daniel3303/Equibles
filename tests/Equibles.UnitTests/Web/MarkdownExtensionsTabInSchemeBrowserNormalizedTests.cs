using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsTabInSchemeBrowserNormalizedTests
{
    // Adversarial Lane A. The sibling tab-in-scheme pin asserts the raw output
    // does NOT contain the literal substring "javascript" — but a href emitted
    // as "java\tscript:alert(1)" (tab between the letters) trivially satisfies
    // that check while still being an ACTIVE javascript: href, because the
    // WHATWG URL spec / every browser strips ASCII tab (U+0009), LF (U+000A)
    // and CR (U+000D) from URL input before scheme parsing. The real contract
    // (stored-XSS prevention on Raw()-rendered ingested content): no active
    // javascript: scheme must survive once the browser's own whitespace
    // stripping is applied. So model the browser: strip tab/LF/CR from the
    // captured HTML, THEN assert no "javascript:" scheme remains.
    [Fact]
    public void MarkdownToHtml_TabInsideJavascriptScheme_NoActiveSchemeAfterBrowserWhitespaceStripping()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[x](<java\tscript:alert(1)>)");

        captured.Should().NotBeNull();
        var browserNormalized = captured.Replace("\t", "").Replace("\n", "").Replace("\r", "");
        browserNormalized
            .Should()
            .NotContain(
                "javascript:",
                "a browser strips tab/LF/CR from URLs before parsing, so a literal-tab 'java<TAB>script:' href resolves to an active javascript: scheme despite passing a naive literal-substring check"
            );
    }
}
