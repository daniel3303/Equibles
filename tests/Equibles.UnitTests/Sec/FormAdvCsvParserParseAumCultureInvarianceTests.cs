using System.Globalization;
using System.Text;
using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class FormAdvCsvParserParseAumCultureInvarianceTests
{
    // ParseAum reads comma-grouped dollar figures such as "2,481,367,832.00".
    // It must use InvariantCulture: under de-DE the comma is the decimal
    // separator and the dot the group separator, so a host-culture parse of
    // that string sees several decimal points, fails, and silently maps the
    // AUM to null. Pin invariance against that regression.
    [Fact]
    public void Parse_CommaGroupedAumUnderGermanCulture_ParsesToWholeDollars()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "FormAdv",
            "ia-sample-2022-04.csv"
        );

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            using var reader = new StreamReader(path, Encoding.Latin1);
            var bnyMellon = FormAdvCsvParser.Parse(reader).Single(a => a.Crd == 231);

            bnyMellon.TotalRegulatoryAum.Should().Be(2_481_367_832L);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
