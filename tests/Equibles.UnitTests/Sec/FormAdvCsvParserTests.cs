using System.Text;
using Equibles.Integrations.Sec.FormAdv;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Record-replay pins for the Form ADV CSV parser, run against a real captured slice of the
/// SEC's bulk download (header + the first four advisers of the April 2022 file). Asserting on
/// exact values catches parser regressions: the comma-formatted regulatory-AUM figures, the
/// quoted legal names that themselves contain commas ("SMITH, BROWN &amp; GROOVER, INC."), the
/// Item 5.E fee flags, and the column-name-based mapping that ignores the 250+ columns the
/// importer does not persist.
/// </summary>
public class FormAdvCsvParserTests
{
    private static List<FormAdvAdviserData> ParseCassette()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "FormAdv",
            "ia-sample-2022-04.csv"
        );
        using var reader = new StreamReader(path, Encoding.Latin1);
        return FormAdvCsvParser.Parse(reader).ToList();
    }

    [Fact]
    public void Parse_RealCapturedSlice_ReturnsAllFourAdvisers()
    {
        var advisers = ParseCassette();

        advisers.Should().HaveCount(4);
        advisers.Select(a => a.Crd).Should().Equal(231, 1249, 1329, 1331);
    }

    [Fact]
    public void Parse_FirstAdviser_MapsIdentityLocationAndAum()
    {
        var bnyMellon = ParseCassette().Single(a => a.Crd == 231);

        bnyMellon.SecNumber.Should().Be("801-54739");
        bnyMellon.LegalName.Should().Be("BNY MELLON SECURITIES CORPORATION");
        bnyMellon.MainOfficeCity.Should().Be("NEW YORK");
        bnyMellon.MainOfficeState.Should().Be("NY");
        bnyMellon.MainOfficeCountry.Should().Be("United States");
        bnyMellon.NumberOfEmployees.Should().Be(333);

        // Comma-formatted dollar figures parse to whole dollars.
        bnyMellon.DiscretionaryAum.Should().Be(829_845_109L);
        bnyMellon.NonDiscretionaryAum.Should().Be(1_651_522_723L);
        bnyMellon.TotalRegulatoryAum.Should().Be(2_481_367_832L);
    }

    [Fact]
    public void Parse_QuotedNameContainingCommas_IsReadAsOneField()
    {
        // The CSV tokenizer must treat "SMITH, BROWN & GROOVER, INC." as a single field rather
        // than splitting on the embedded commas, or every later column shifts left and the AUM
        // figures land in the wrong place.
        var smithBrown = ParseCassette().Single(a => a.Crd == 1329);

        smithBrown.LegalName.Should().Be("SMITH, BROWN & GROOVER, INC.");
        smithBrown.MainOfficeState.Should().Be("GA");
        smithBrown.TotalRegulatoryAum.Should().Be(199_946_002L);
    }

    [Fact]
    public void Parse_FeeStructureFlags_MapYesNoToBooleans()
    {
        var advisers = ParseCassette();

        // James I. Black & Company reports several fee types (5E columns Y/N/Y/N/N/N/Y).
        var jamesBlack = advisers.Single(a => a.Crd == 1249);
        jamesBlack.ChargesPercentageOfAum.Should().BeTrue();
        jamesBlack.ChargesHourly.Should().BeTrue();
        jamesBlack.ChargesSubscription.Should().BeFalse();
        jamesBlack.ChargesFixed.Should().BeTrue();
        jamesBlack.ChargesOther.Should().BeTrue();

        // BNY Mellon reports only percentage-of-AUM compensation.
        var bnyMellon = advisers.Single(a => a.Crd == 231);
        bnyMellon.ChargesPercentageOfAum.Should().BeTrue();
        bnyMellon.ChargesHourly.Should().BeFalse();
        bnyMellon.ChargesPerformanceBased.Should().BeFalse();
    }
}
