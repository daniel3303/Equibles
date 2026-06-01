using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial pin for <see cref="FormAdvCsvParser"/>'s AUM parsing. The parser is documented as
/// defensive — it nulls a field it cannot represent ("Returns null for a blank cell") and skips
/// unkeyable rows — so one malformed cell in the untrusted SEC CSV must never abort the whole
/// streaming import. A figure with more digits than <see cref="long"/> can hold still parses as a
/// <see cref="decimal"/>, so the narrowing cast to long can overflow; the contract requires that
/// to degrade to "not reported" (null), not throw.
/// </summary>
public class FormAdvCsvParserParseAumOverflowTests
{
    [Fact]
    public void Parse_TotalAumLargerThanLongRange_TreatsCellAsNotReportedInsteadOfThrowing()
    {
        // 26 digits: within decimal's range but far beyond long.MaxValue (~9.2e18).
        var csv = "Organization CRD#,5F(2)(c)\n" + "12345,99999999999999999999999999\n";

        var advisers = FormAdvCsvParser.Parse(new StringReader(csv)).ToList();

        advisers.Should().ContainSingle();
        advisers[0].Crd.Should().Be(12345);
        advisers[0].TotalRegulatoryAum.Should().BeNull();
    }
}
