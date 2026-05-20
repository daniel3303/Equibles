using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsFragmentUrlTests
{
    // Contract (IsSafeUrl XML-doc: "Relative URLs and fragments (no scheme) are
    // left intact"): a markdown link to a #fragment has no scheme, so the
    // sanitizer must leave its URL untouched. Sibling to the javascript:,
    // entity-encoded-scheme, and raw-HTML pins — together they pin the
    // sanitizer's positive and negative cases. A regression that clobbered
    // no-scheme URLs would break every in-app anchor rendered from stored
    // markdown (CHANGELOG, transcripts, on-page TOC links).
    [Fact]
    public void MarkdownToHtml_LinkToFragmentWithoutScheme_PreservesFragmentInHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[Jump to section](#overview)");

        captured.Should().NotBeNull();
        captured.Should().Contain("href=\"#overview\"");
    }
}
