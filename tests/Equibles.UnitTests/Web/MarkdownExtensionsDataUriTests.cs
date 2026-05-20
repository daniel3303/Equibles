using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsDataUriTests
{
    // Sibling to MarkdownExtensionsJavascriptUriTests. The production WHY-comment
    // on `IsSafeUrl` enumerates THREE XSS-prone schemes — javascript:, data:, and
    // vbscript: — that .DisableHtml() does NOT defend against because Markdig
    // treats link destinations as opaque text. Only `javascript:` is pinned.
    // `data:text/html,<script>alert('xss')</script>` is a modern stored-XSS
    // vector that bypasses CSPs missing `data:` in their `default-src` and is
    // exactly the kind of payload a SEC filing / transcript ingest could carry.
    // A refactor that "ergonomically" adds `data` to AllowedUriSchemes (e.g. to
    // enable inline base64 images) would silently let active data: hrefs into
    // the rendered DOM.
    [Fact]
    public void MarkdownToHtml_DataUriInLink_DoesNotEmitActiveDataHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml(
            "[click me](data:text/html,<script>alert(document.cookie)</script>)"
        );

        captured.Should().NotBeNull();
        captured.Should().NotContain("href=\"data:");
    }
}
