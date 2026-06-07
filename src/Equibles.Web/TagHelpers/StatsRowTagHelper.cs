using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("stats-row")]
public class StatsRowTagHelper : TagHelper
{
    // DaisyUI responsive breakpoint at which the stat tiles switch from a
    // vertical stack to a horizontal row. Only "sm" and "lg" are used today.
    [HtmlAttributeName("breakpoint")]
    public string Breakpoint { get; set; } = "lg";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Full literal class strings (no interpolation) so Tailwind's content
        // scanner detects both responsive variants in this file.
        var css =
            Breakpoint == "sm"
                ? "stats stats-vertical sm:stats-horizontal shadow-sm bg-base-200 w-full"
                : "stats stats-vertical lg:stats-horizontal shadow-sm bg-base-200 w-full";
        output.Attributes.SetAttribute("class", css);
    }
}
