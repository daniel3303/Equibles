using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane A (adversarial): Sign=true with a negative value must render a "-"
// sign prefix before the dollar prefix, and the displayed number must be the
// absolute value — not double-negated or missing the sign.
public class CompactNumberTagHelperNegativeSignTests
{
    [Fact]
    public void Process_NegativeValueWithSignEnabled_ShowsMinusBeforePrefix()
    {
        var sut = new CompactNumberTagHelper
        {
            Value = -42_500m,
            Prefix = "$",
            Sign = true,
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
        output.Content.GetContent().Should().Be("-$42,500");
        output.Attributes["data-compact-number"].Value.Should().Be("42500");
        output.Attributes["data-compact-prefix"].Value.Should().Be("-$");
    }
}
