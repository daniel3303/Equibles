using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay against a real Schedule 13G filed as XML on 2023-12-28, a year before XML became
// mandatory: Regal Nutra's 13G on Collective Audience. Voluntary XML filings use the same schema,
// so the coverage walk starts at the first of them rather than at the 2024-12-18 mandate.
public class Filing13DGXmlParserVoluntaryXmlTests
{
    [Fact]
    public void ParseFiling_VoluntaryXmlFiledBeforeTheMandate_ReadsThePosition()
    {
        var xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Holdings13DG",
                "sc13g-voluntary-xml-2023.xml"
            )
        );

        var result = new Filing13DGXmlParser().ParseFiling(
            xml,
            accessionNumber: "0002006228-23-000001",
            cik: "2006228",
            filingDate: new DateOnly(2023, 12, 28)
        );

        result.FilerCik.Should().Be("2006228");
        result.DateOfEvent.Should().Be(new DateOnly(2023, 12, 7));
        result.IssuerCik.Should().Be("1854583");
        result.IssuerCusip.Should().Be("193939105");
        result.ReportingPersons.Should().ContainSingle();
        result.ReportingPersons[0].AggregateAmountOwned.Should().Be(700_000);
        result.ReportingPersons[0].PercentOfClass.Should().Be(5.3m);
    }
}
