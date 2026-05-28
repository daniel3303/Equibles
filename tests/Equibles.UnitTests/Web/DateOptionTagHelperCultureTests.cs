using System.Globalization;
using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

// Lane A (adversarial): the rendered <option value> is posted back and parsed
// as a date server-side, so it must be a culture-invariant ISO-8601 Gregorian
// date. Thai culture defaults to the Buddhist calendar (year + 543), so a
// culture-dependent ToString would emit "2567-01-15" and break the round-trip.
public class DateOptionTagHelperCultureTests
{
    [Fact(Skip = "GH-2654 — DateOptionTagHelper emits option value in the host-locale calendar")]
    public void Process_UnderNonGregorianCulture_EmitsInvariantIsoDateValue()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            var sut = new DateOptionTagHelper { Date = new DateOnly(2024, 1, 15) };
            var context = new TagHelperContext(
                new TagHelperAttributeList(),
                new Dictionary<object, object>(),
                "test-id"
            );
            var output = new TagHelperOutput(
                "date-option",
                new TagHelperAttributeList(),
                (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
            );

            sut.Process(context, output);

            output.Attributes["value"].Value.Should().Be("2024-01-15");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
