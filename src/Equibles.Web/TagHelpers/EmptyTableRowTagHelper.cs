using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("empty-row")]
public class EmptyTableRowTagHelper : TagHelper
{
    [HtmlAttributeName("colspan")]
    public int Colspan { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "tr";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.PreContent.SetHtmlContent(
            $"<td colspan=\"{Colspan}\" class=\"text-center py-10 text-base-content/60\">"
        );
        output.PostContent.SetHtmlContent("</td>");
    }
}
