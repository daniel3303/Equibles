using System.IO;
using System.Text.Encodings.Web;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class BreadcrumbsTagHelperCustomClassTests
{
    // Contract: a caller-supplied `class` is APPENDED to the default breadcrumb classes,
    // never a replacement — and the inner content is wrapped in a <ul>. The plausible
    // regression: a refactor that sets class straight to CssClass (dropping the defaults
    // under the intuition "the caller passed a class, use it") would compile and strip the
    // shared breadcrumb styling from every page that customises the class. Derive the oracle
    // from the merge contract: defaults stay, custom is added.
    [Fact]
    public async Task ProcessAsync_CustomClass_AppendsToDefaultBreadcrumbClasses()
    {
        var sut = new BreadcrumbsTagHelper { CssClass = "mt-4" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "breadcrumbs",
            new TagHelperAttributeList(),
            (_, _) =>
            {
                var content = new DefaultTagHelperContent();
                content.AppendHtml("<li>Home</li>");
                return Task.FromResult<TagHelperContent>(content);
            }
        );

        await sut.ProcessAsync(context, output);

        var cssClass = output.Attributes["class"].Value.ToString();
        cssClass.Should().Be("breadcrumbs text-sm text-base-content/60 mt-4");
        Render(output.Content).Should().Be("<ul><li>Home</li></ul>");
    }

    private static string Render(TagHelperContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
