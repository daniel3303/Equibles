using Equibles.GovernmentContracts.HostedService.Canada;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Reads recorded open.canada.ca datastore answers (Fixtures/README.md): the rows a quarter holds, the identity
// one contract keeps across its amendments, and the firewall page the portal sends instead of JSON.
public class OpenCanadaContractsTests
{
    [Fact]
    public void ParsePage_ReadsEveryReportOfTheQuarter()
    {
        var rows = OpenCanadaContracts.ParsePage(
            Fixture("open-canada-contracts-kbl-2026-2027-Q2.json")
        );

        rows.Should().HaveCount(3);
        var amendment = rows.Single(row => row.ReferenceNumber == "C-2026-2027-Q2-02168");
        amendment
            .Should()
            .BeEquivalentTo(
                new
                {
                    Department = "pwgsc-tpsgc",
                    ProcurementId = "CW2439844",
                    VendorName = "KBL PROJECTS LTD.",
                    ContractDate = "2026-06-11",
                    ContractValue = 1495407.84m,
                    InstrumentType = "A",
                    ReportingPeriod = "2026-2027-Q2",
                }
            );
        amendment.AwardUniqueKey.Should().Be("CA:pwgsc-tpsgc:CW2439844");
        amendment.ReferenceSequence.Should().Be(2168);
        OpenCanadaContracts
            .EnglishTitle(amendment.DepartmentTitle)
            .Should()
            .Be("Public Services and Procurement Canada");
    }

    [Fact]
    public void ParsePage_RefusesTheFirewallPageRatherThanReadingAnEmptyQuarter()
    {
        var parse = () => OpenCanadaContracts.ParsePage(Fixture("open-canada-rejected.html"));

        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("2026-11-03", "2026-2027-Q3", "2026-10-01", "2026-12-31")]
    [InlineData("2026-04-01", "2026-2027-Q1", "2026-04-01", "2026-06-30")]
    [InlineData("2027-03-31", "2026-2027-Q4", "2027-01-01", "2027-03-31")]
    [InlineData("2026-09-30", "2026-2027-Q2", "2026-07-01", "2026-09-30")]
    public void ReportingPeriods_FollowTheFederalFiscalYear(
        string day,
        string code,
        string start,
        string end
    )
    {
        var period = CanadianReportingPeriod.Containing(DateOnly.Parse(day));

        period.Code.Should().Be(code);
        period.Start.Should().Be(DateOnly.Parse(start));
        period.End.Should().Be(DateOnly.Parse(end));
        period.Next.Previous.Should().Be(period);
    }

    [Theory]
    [InlineData("2026-06-11", "2026-06-11")]
    [InlineData("1899-12-30", null)]
    [InlineData("2026-12-01", null)]
    [InlineData("not a date", null)]
    public void AwardDate_DropsPlaceholdersAndDatesAfterToday(string value, string expected)
    {
        OpenCanadaContracts
            .AwardDate(value, new DateOnly(2026, 11, 3))
            .Should()
            .Be(expected == null ? null : DateOnly.Parse(expected));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "GovernmentContracts", "Fixtures", name)
        );
}
