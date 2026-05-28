using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsAutolinkVbscriptUriTests
{
    // The GH-2624 fix neutralizes unsafe-scheme autolinks (`<scheme:body>`,
    // an AutolinkInline node, not LinkInline) via the SAME IsSafeUrl allowlist
    // as link/image destinations. The javascript: autolink arm is pinned by
    // MarkdownExtensionsAutolinkJavascriptUriTests; the issue explicitly called
    // out vbscript: and data: as the same bypass class. The contract: every
    // non-allowlisted scheme reaching the autolink path must be stripped of its
    // active href, not just javascript:. A refactor that special-cased
    // javascript: instead of relying on the allowlist would leak vbscript:.
    [Fact]
    public void MarkdownToHtml_VbscriptAutolink_DoesNotEmitActiveVbscriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("<vbscript:msgbox(1)>");

        captured.Should().NotBeNull();
        captured.Should().NotContain("href=\"vbscript:");
    }
}
