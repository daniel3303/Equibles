using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Twin of <see cref="FormAdvCsvParserParseAumOverflowTests"/> for the negative end of the range.
/// ParseAum is documented as defensive — it nulls a field it cannot represent — so a figure more
/// negative than <see cref="long"/>.MinValue (still a valid decimal) must degrade to "not reported"
/// (null) rather than overflow the narrowing cast and abort the whole streaming import.
/// </summary>
public class FormAdvCsvParserParseAumUnderflowTests
{
    [Fact]
    public void Parse_TotalAumBelowLongRange_TreatsCellAsNotReportedInsteadOfThrowing()
    {
        // 26-digit negative: within decimal's range but far below long.MinValue (~-9.2e18).
        var csv = "Organization CRD#,5F(2)(c)\n" + "12345,-99999999999999999999999999\n";

        var advisers = FormAdvCsvParser.Parse(new StringReader(csv)).ToList();

        advisers.Should().ContainSingle();
        advisers[0].Crd.Should().Be(12345);
        advisers[0].TotalRegulatoryAum.Should().BeNull();
    }
}
