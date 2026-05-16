using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsJavascriptUriTests
{
    // Contract (security intent established by the sibling raw-<script> pin's own
    // comment): MarkdownToHtml renders untrusted ingested content (SEC filings,
    // earnings transcripts) via htmlHelper.Raw() — unencoded — and uses
    // .DisableHtml() so "potentially malicious payloads" never reach the DOM.
    // .DisableHtml() neutralizes raw HTML tags but NOT link destinations:
    // `[x](javascript:…)` is a well-known XSS bypass the existing tests don't
    // cover. Per the stated anti-XSS purpose, the output must not carry an
    // active javascript: href. (Ambiguity: contract is implicit security intent,
    // not an XML-doc — but Raw() + DisableHtml + ingested input make it the
    // behaviour a caller must rely on.)
    [Fact(Skip = "GH-703 — MarkdownToHtml emits active javascript: link href (stored XSS)")]
    public void MarkdownToHtml_JavascriptUriInLink_DoesNotEmitActiveJavascriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[click me](javascript:alert(document.cookie))");

        captured.Should().NotBeNull();
        captured.Should().NotContain("href=\"javascript:");
    }
}
