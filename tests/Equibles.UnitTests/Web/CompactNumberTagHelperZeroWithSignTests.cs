using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class CompactNumberTagHelperZeroWithSignTests
{
    [Fact]
    public void Process_ZeroValueWithSignEnabled_EmitsNoSignPrefix()
    {
        // Sibling to NegativeSign / positive-no-sign pins. The Sign ternary
        // (CompactNumberTagHelper.cs:21-27) is `Value > 0 ? "+" : Value < 0
        // ? "-" : ""` — STRICT greater-than/less-than zero. A refactor to
        // `Value >= 0 ? "+" : "-"` (intuitive when the cascade looks
        // verbose) would compile, pass both existing pins (positive uses
        // Sign=false; negative uses Sign=true with a non-zero value), and
        // silently emit "+0" for every zero-valued cell that opts into the
        // sign suffix — visually confusing in change-percent displays
        // (a flat 0% would render as "+0%", suggesting "barely positive").
        // Pin the strict-zero arm via the rendered content AND the
        // data-compact-prefix attribute (which is set only when displayPrefix
        // is non-empty — its absence proves no "+" was prepended).
        var sut = new CompactNumberTagHelper { Value = 0m, Sign = true };
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

        output.Content.GetContent().Should().Be("0");
        output.Attributes.ContainsName("data-compact-prefix").Should().BeFalse();
    }
}
