using System.Text.Encodings.Web;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class HtmlExtensionsJsonScriptBreakoutTests
{
    // Contract: Html.Json<T> emits its result through HtmlString (raw, unencoded)
    // for embedding inside a Razor <script> block, so the serialized JSON itself
    // must be safe in that context. A string value containing "</script>" must not
    // survive verbatim — the '<' has to be escaped (<) or it closes the host
    // <script> element and turns serialized model data into stored XSS.
    [Fact]
    public void Json_StringContainingScriptClosingTag_EscapesAngleBrackets()
    {
        var html = Substitute.For<IHtmlHelper>();

        var content = html.Json("</script><script>alert(1)</script>");

        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        var rendered = writer.ToString();

        rendered.Should().NotContain("<");
        rendered.Should().Contain("\\u003C");
    }
}
