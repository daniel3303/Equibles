using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class FormAdvCsvParserParseAumMidpointRoundingTests
{
    // ParseAum converts a fractional AUM figure to whole dollars. A half-dollar
    // is the ambiguous case: a caller reading "100.50" as dollars expects the
    // half to round up to 101. The integer part here (100) is even, so the
    // default banker's rounding (ToEven) would instead round DOWN to 100 — this
    // input therefore pins the away-from-zero contract and fails if the rounding
    // mode is ever dropped to the Math.Round default.
    [Fact]
    public void Parse_AumWithHalfDollar_RoundsAwayFromZero()
    {
        var csv = "Organization CRD#,5F(2)(c)\n777,100.50\n";
        using var reader = new StringReader(csv);

        var adviser = FormAdvCsvParser.Parse(reader).Single();

        adviser.TotalRegulatoryAum.Should().Be(101L);
    }
}
