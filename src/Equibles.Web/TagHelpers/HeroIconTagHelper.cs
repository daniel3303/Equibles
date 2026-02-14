using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("icon", TagStructure = TagStructure.WithoutEndTag)]
public class HeroIconTagHelper : TagHelper {
    [HtmlAttributeName("name")]
    public string Name { get; set; }

    [HtmlAttributeName("solid")]
    public bool Solid { get; set; }

    [HtmlAttributeName("class")]
    public string CssClass { get; set; }

    [HtmlAttributeName("size")]
    public string Size { get; set; } = "6";

    public override void Process(TagHelperContext context, TagHelperOutput output) {
        var style = Solid ? HeroIcons.IconStyle.Solid : HeroIcons.IconStyle.Outline;
        var svg = HeroIcons.Render(Name, style, Size, CssClass);

        if (string.IsNullOrEmpty(svg)) {
            output.SuppressOutput();
            return;
        }

        output.TagName = null;
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.SetHtmlContent(svg);
    }
}
