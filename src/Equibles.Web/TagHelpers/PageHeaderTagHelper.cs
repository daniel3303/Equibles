using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("page-header")]
public class PageHeaderTagHelper : TagHelper
{
    [HtmlAttributeName("title")]
    public string Title { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var subtitle = await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("class", "mb-8");

        output.Content.AppendHtml("<h1 class=\"text-3xl font-bold mb-2\">");
        output.Content.Append(Title);
        output.Content.AppendHtml("</h1>");
        output.Content.AppendHtml("<p class=\"text-base-content/60\">");
        output.Content.AppendHtml(subtitle);
        output.Content.AppendHtml("</p>");
    }
}
