using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsImageJavascriptUriTests
{
    // The IsSafeUrl WHY-comment explicitly enumerates "a markdown link/image
    // with a javascript:/data:/vbscript: scheme" as the risk. All three
    // existing sibling tests cover the LINK form ([text](url) → href=);
    // the IMAGE form (![alt](url) → src=) is unpinned. The implementation
    // covers both today because Markdig represents inline images as
    // LinkInline with IsImage=true, so a single Descendants<LinkInline>()
    // walk neutralises them — but a refactor that filters that walk to
    // `!link.IsImage`, or splits image rendering into a separate pass, would
    // silently leak javascript: through <img src> with no test catching it.
    [Fact]
    public void MarkdownToHtml_JavascriptUriInImage_DoesNotEmitActiveJavascriptSrc()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("![logo](javascript:alert(\"xss\"))");

        captured.Should().NotBeNull();
        captured.Should().NotContain("src=\"javascript:");
    }
}
