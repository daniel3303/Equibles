using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("date-option", TagStructure = TagStructure.WithoutEndTag)]
public class DateOptionTagHelper : TagHelper
{
    [HtmlAttributeName("date")]
    public DateOnly Date { get; set; }

    [HtmlAttributeName("selected")]
    public bool Selected { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "option";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("value", Date.ToString("yyyy-MM-dd"));
        if (Selected)
            output.Attributes.SetAttribute("selected", "selected");
        output.Content.SetContent(Date.ToString("MMM dd, yyyy"));
    }
}
