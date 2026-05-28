using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsAutolinkJavascriptUriTests
{
    // The sibling `[x](javascript:…)` pin establishes the contract: MarkdownToHtml
    // renders untrusted ingested content and must never emit an active javascript:
    // href. Markdig's autolink syntax `<scheme:body>` is a second URL-producing
    // construct in the same grammar and falls under the same contract — the AST
    // node is AutolinkInline, not LinkInline, so it bypasses any sanitizer that
    // only walks LinkInline descendants.
    [Fact(Skip = "GH-2624 — MarkdownToHtml emits active javascript: href for autolink syntax")]
    public void MarkdownToHtml_JavascriptAutolink_DoesNotEmitActiveJavascriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("<javascript:alert(document.cookie)>");

        captured.Should().NotBeNull();
        captured.Should().NotContain("href=\"javascript:");
    }
}
