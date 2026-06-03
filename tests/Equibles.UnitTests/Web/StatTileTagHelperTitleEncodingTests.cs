using System.IO;
using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class StatTileTagHelperTitleEncodingTests
{
    // Contract: the stat-title label is written with IHtmlContentBuilder.Append — the
    // HTML-ENCODING overload — not AppendHtml. A Title carrying markup must therefore be
    // emitted encoded. The plausible regression this catches: a refactor that "tidies" the
    // Title write to AppendHtml (matching the surrounding AppendHtml calls) would compile,
    // render identically for plain text, and open stored XSS for any title sourced from user
    // or upstream data. Derive the oracle from the encoding contract, not the body.
    [Fact]
    public async Task ProcessAsync_TitleWithMarkup_HtmlEncodesTitleLabel()
    {
        var sut = new StatTileTagHelper { Title = "<script>alert(1)</script>" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "stat-tile",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        await sut.ProcessAsync(context, output);

        var content = Render(output.Content);
        content.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        content.Should().NotContain("<script>");
    }

    private static string Render(TagHelperContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
