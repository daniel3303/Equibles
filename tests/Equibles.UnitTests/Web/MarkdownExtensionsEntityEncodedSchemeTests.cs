using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class MarkdownExtensionsEntityEncodedSchemeTests
{
    // Contract (stated anti-XSS intent): MarkdownToHtml renders untrusted
    // ingested content via Raw() and must neutralize dangerous link schemes.
    // The scheme allowlist regex requires a literal `scheme:`; an entity-encoded
    // colon (`javascript&#58;…`) defeats the regex so IsSafeUrl returns true and
    // the URL is left intact, but a browser HTML-decodes the href attribute back
    // to `javascript:` — so the output must not carry that scheme in any form.
    [Fact]
    public void MarkdownToHtml_EntityEncodedColonInJavascriptScheme_DoesNotEmitJavascriptHref()
    {
        string captured = null;
        var htmlHelper = Substitute.For<IHtmlHelper>();
        htmlHelper
            .Raw(Arg.Do<string>(s => captured = s))
            .Returns(callInfo => new HtmlString(callInfo.Arg<string>()));

        htmlHelper.MarkdownToHtml("[x](javascript&#58;alert(1))");

        captured.Should().NotBeNull();
        captured.Should().NotContain("javascript");
    }
}
