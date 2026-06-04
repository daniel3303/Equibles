using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

// Wraps tabular content in the standard vertical-scroll (max-h) + zebra-table shell
// used by the market and economic-data history tables, so the shared markup lives in
// one place. Differs from <table-card> (no card wrapper, scrollable height, aria-label).
[HtmlTargetElement("scroll-table")]
public class ScrollTableTagHelper : TagHelper
{
    [HtmlAttributeName("aria-label")]
    public string AriaLabel { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "overflow-x-auto max-h-[400px] overflow-y-auto");
        var label = HtmlEncoder.Default.Encode(AriaLabel ?? string.Empty);
        output.PreContent.SetHtmlContent(
            $"<table class=\"table table-zebra table-sm\" aria-label=\"{label}\">"
        );
        output.PostContent.SetHtmlContent("</table>");
    }
}
