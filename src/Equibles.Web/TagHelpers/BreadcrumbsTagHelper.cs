using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("breadcrumbs")]
public class BreadcrumbsTagHelper : TagHelper
{
    [HtmlAttributeName("class")]
    public string CssClass { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var inner = await output.GetChildContentAsync();

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        var cssClass = string.IsNullOrWhiteSpace(CssClass)
            ? "breadcrumbs text-sm text-base-content/60"
            : $"breadcrumbs text-sm text-base-content/60 {CssClass}";
        output.Attributes.SetAttribute("class", cssClass);
        output.Content.SetHtmlContent("<ul>");
        output.Content.AppendHtml(inner);
        output.Content.AppendHtml("</ul>");
    }
}
