using System.Globalization;
using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class SignedFormattingToStringWithSignCultureTests
{
    // Contract: numeric output across Equibles.Web is invariant-formatted (every
    // sibling formatter — CompactNumberTagHelper, CsvExportService, the export
    // controllers — passes CultureInfo.InvariantCulture). A value rendered through
    // ToStringWithSign in a view must therefore use '.' as the decimal separator and
    // ',' as the grouping separator regardless of the host's thread culture, so the
    // figure reads the same next to its invariant-formatted neighbours.
    [Fact]
    public void ToStringWithSign_UnderCommaDecimalCulture_UsesInvariantSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            (1234.5).ToStringWithSign("N2").Should().Be("+1,234.50");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
