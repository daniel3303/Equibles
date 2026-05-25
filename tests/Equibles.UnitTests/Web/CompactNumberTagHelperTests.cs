using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane B (coverage): pins CompactNumberTagHelper.Process — tag name, data
// attributes, sign-prefix handling, and N0 formatting. All 21 lines were
// zero-hit in unit coverage.
public class CompactNumberTagHelperTests
{
    [Fact]
    public void Process_PositiveValueWithPrefix_RendersSpanWithDataAttributes()
    {
        var sut = new CompactNumberTagHelper
        {
            Value = 1_234_567m,
            Prefix = "$",
            Sign = false,
        };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "compactable-number",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        sut.Process(context, output);

        output.TagName.Should().Be("span");
        output.TagMode.Should().Be(TagMode.StartTagAndEndTag);
        output.Content.GetContent().Should().Be("$1,234,567");
        output.Attributes["data-compact-number"].Value.Should().Be("1234567");
        output.Attributes["data-compact-prefix"].Value.Should().Be("$");
    }
}
