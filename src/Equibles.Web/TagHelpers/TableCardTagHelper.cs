using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

// Wraps tabular content in the standard card + horizontal-scroll + zebra-table shell
// used across the stock and profile views, so the shared markup lives in one place.
[HtmlTargetElement("table-card")]
public class TableCardTagHelper : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "card bg-base-100 shadow-sm");
        output.PreContent.SetHtmlContent(
            "<div class=\"overflow-x-auto\"><table class=\"table table-zebra table-sm\">"
        );
        output.PostContent.SetHtmlContent("</table></div>");
    }
}
