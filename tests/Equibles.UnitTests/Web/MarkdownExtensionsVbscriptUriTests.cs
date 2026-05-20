using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsVbscriptUriTests
{
    // Third and final sibling in the XSS-scheme family. The production WHY-comment
    // on `IsSafeUrl` enumerates THREE schemes that `.DisableHtml()` does NOT
    // defend against — javascript:, data:, and vbscript:. The javascript: arm
    // is pinned by MarkdownExtensionsJavascriptUriTests, the data: arm by
    // MarkdownExtensionsDataUriTests; vbscript: was the only one of the three
    // not yet pinned.
    //
    // vbscript: is the historical IE-only XSS vector (active in Office/Outlook
    // HTML rendering surfaces and in some old WebView2 contexts). Untrusted
    // ingest from earnings transcripts / SEC filings can still carry payloads
    // crafted for those environments — keep all three documented schemes
    // locked down. A refactor that swaps `AllowedUriSchemes` to a different
    // sanitiser path and forgets to deny vbscript: would leak past the
    // existing two-scheme test net.
    [Fact]
    public void MarkdownToHtml_VbscriptUriInLink_DoesNotEmitActiveVbscriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[click me](vbscript:MsgBox(\"xss\"))");

        captured.Should().NotBeNull();
        captured.Should().NotContain("href=\"vbscript:");
    }
}
