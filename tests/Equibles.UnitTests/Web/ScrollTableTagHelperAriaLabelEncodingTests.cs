using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane A (adversarial): ScrollTableTagHelper interpolates AriaLabel straight into the
// rendered aria-label="..." attribute, so it must HTML-encode the value. A label
// containing a double quote would otherwise break out of the attribute and inject
// markup; the contract is that the dangerous quote is encoded, never emitted raw.
public class ScrollTableTagHelperAriaLabelEncodingTests
{
    [Fact]
    public void Process_AriaLabelContainsDoubleQuote_EncodesItSoItCannotBreakOutOfTheAttribute()
    {
        var sut = new ScrollTableTagHelper { AriaLabel = "Prices for \"ACME\"" };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "scroll-table",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        sut.Process(context, output);

        var pre = output.PreContent.GetContent();
        pre.Should().Contain("&quot;ACME&quot;");
        // A raw quote around ACME would prematurely close aria-label and open an injection point.
        pre.Should().NotContain("\"ACME\"");
    }
}
