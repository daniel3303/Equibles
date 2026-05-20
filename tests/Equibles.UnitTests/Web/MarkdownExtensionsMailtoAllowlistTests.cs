using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsMailtoAllowlistTests
{
    // The production `AllowedUriSchemes` set enumerates THREE positive schemes:
    // http, https, mailto. Every existing MarkdownExtensions sibling test pins
    // a NEGATIVE arm (javascript/data/vbscript/image rejected). The positive
    // arms for http/https are exercised implicitly via the general rendering
    // tests, but `mailto` has no pin — a refactor that drops it from the
    // allowlist (e.g. "we only need web links") would silently strip every
    // `mailto:` link from rendered markdown with no test net to catch it,
    // breaking the "contact via email" links in earnings transcripts and
    // SEC filings the dashboard renders.
    [Fact]
    public void MarkdownToHtml_MailtoLink_PreservesActiveMailtoHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[email us](mailto:contact@example.com)");

        captured.Should().NotBeNull();
        captured.Should().Contain("href=\"mailto:contact@example.com\"");
    }
}
