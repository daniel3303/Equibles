using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class FormAdvCsvParserParseAumReportedZeroTests
{
    // ParseAum documents that a blank cell returns null so "not reported" stays
    // distinguishable from a reported zero. This pins the other side of that
    // contract: an adviser reporting exactly "0.00" AUM must map to 0L, not null.
    // A regression coalescing 0 into null would erase the reported-zero signal.
    [Fact]
    public void Parse_ReportedZeroAum_MapsToZeroNotNull()
    {
        var csv = "Organization CRD#,5F(2)(c)\n777,0.00\n";
        using var reader = new StringReader(csv);

        var adviser = FormAdvCsvParser.Parse(reader).Single();

        adviser.TotalRegulatoryAum.Should().Be(0L);
    }
}
