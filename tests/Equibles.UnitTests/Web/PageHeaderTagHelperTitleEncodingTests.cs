using System.IO;
using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class PageHeaderTagHelperTitleEncodingTests
{
    // Contract: the page-header `title` is plain data — company/profile names routinely
    // carry an ampersand ("AT&T", "Procter & Gamble") — so it must be HTML-ENCODED into the
    // <h1>, not written as raw markup. The implementation uses Content.Append(Title) (which
    // encodes) rather than AppendHtml (which would not). The plausible regression: a refactor
    // swapping Append -> AppendHtml "because it's all HTML anyway" would compile, render names
    // with raw '&'/'<' (broken display / stored XSS for any data-driven title). Derive the
    // oracle from the encoding contract: '&' -> &amp;, '<script>' -> &lt;script&gt;.
    [Fact]
    public async Task ProcessAsync_TitleWithHtmlSpecialCharacters_HtmlEncodesIntoHeading()
    {
        var sut = new PageHeaderTagHelper { Title = "AT&T <script>" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "page-header",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        await sut.ProcessAsync(context, output);

        var rendered = Render(output.Content);
        rendered
            .Should()
            .Contain("<h1 class=\"text-3xl font-bold mb-2\">AT&amp;T &lt;script&gt;</h1>");
        rendered.Should().NotContain("<script>");
    }

    private static string Render(TagHelperContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
