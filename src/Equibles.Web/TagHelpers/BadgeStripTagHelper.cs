using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

// Wraps a horizontal strip of muted metadata badges (the gap-3 flex row that sits
// above a card's stats/body). Centralises the class combo so the three Profiles
// summary cards don't drift apart.
[HtmlTargetElement("badge-strip")]
public class BadgeStripTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute(
            "class",
            "flex flex-wrap items-center gap-3 mb-3 text-xs text-base-content/60"
        );

        output.Content.SetHtmlContent(inner);
    }
}
