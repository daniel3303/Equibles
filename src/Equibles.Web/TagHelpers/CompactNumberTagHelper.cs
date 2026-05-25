using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.Web.TagHelpers;

[HtmlTargetElement("compactable-number", TagStructure = TagStructure.WithoutEndTag)]
public class CompactNumberTagHelper : TagHelper
{
    [HtmlAttributeName("value")]
    public decimal Value { get; set; }

    [HtmlAttributeName("prefix")]
    public string Prefix { get; set; }

    [HtmlAttributeName("sign")]
    public bool Sign { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var absValue = Math.Abs(Value);
        var signPrefix = Sign
            ? Value > 0
                ? "+"
                : Value < 0
                    ? "-"
                    : ""
            : "";
        var displayPrefix = signPrefix + (Prefix ?? "");
        var formatted = displayPrefix + absValue.ToString("N0", CultureInfo.InvariantCulture);

        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute(
            "data-compact-number",
            absValue.ToString(CultureInfo.InvariantCulture)
        );
        if (!string.IsNullOrEmpty(displayPrefix))
            output.Attributes.SetAttribute("data-compact-prefix", displayPrefix);
        output.Content.SetContent(formatted);
    }
}
