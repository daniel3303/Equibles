using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("stat-tile")]
public class StatTileTagHelper : TagHelper
{
    [HtmlAttributeName("title")]
    public string Title { get; set; }

    // Maps to the outer div's native `title` attribute (browser tooltip);
    // a separate attribute name avoids colliding with the inner stat-title label.
    [HtmlAttributeName("tooltip")]
    public string Tooltip { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("class", "stat");
        if (!string.IsNullOrEmpty(Tooltip))
            output.Attributes.SetAttribute("title", Tooltip);

        output.Content.AppendHtml("<div class=\"stat-title\">");
        output.Content.Append(Title);
        output.Content.AppendHtml("</div>");
        output.Content.AppendHtml("<div class=\"stat-value text-2xl font-mono\">");
        output.Content.AppendHtml(inner);
        output.Content.AppendHtml("</div>");
    }
}
