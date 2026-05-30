using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the Form ADV MCP tools against ParadeDB. The search tool runs an <c>ILIKE</c> query that
/// has no in-memory translation, so the real provider is required; the assertions also cover the
/// markdown the tools hand back to an assistant — AUM grouping, fee-structure rendering, and the
/// largest-assets-first ordering.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InvestmentAdviserToolsTests : ParadeDbMcpTestBase
{
    public InvestmentAdviserToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private InvestmentAdviserTools CreateTools() =>
        new(
            new FormAdvAdviserRepository(DbContext),
            errorManager: null,
            NullLogger<InvestmentAdviserTools>()
        );

    private async Task Seed()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .AddRange(
                new FormAdvAdviser
                {
                    Crd = 231,
                    SecNumber = "801-54739",
                    LegalName = "BNY MELLON SECURITIES CORPORATION",
                    PrimaryBusinessName = "BNY MELLON",
                    MainOfficeCity = "NEW YORK",
                    MainOfficeState = "NY",
                    MainOfficeCountry = "United States",
                    NumberOfEmployees = 333,
                    TotalRegulatoryAum = 2_481_367_832L,
                    DiscretionaryAum = 829_845_109L,
                    NonDiscretionaryAum = 1_651_522_723L,
                    ChargesPercentageOfAum = true,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 999,
                    LegalName = "MELLON CAPITAL SMALL",
                    PrimaryBusinessName = "MELLON SMALL",
                    MainOfficeState = "CA",
                    TotalRegulatoryAum = 10_000_000L,
                    ChargesHourly = true,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SearchInvestmentAdvisers_BlankQuery_PromptsForName()
    {
        var result = await CreateTools().SearchInvestmentAdvisers("  ");

        result.Should().Contain("Provide part of an adviser's name");
    }

    [Fact]
    public async Task SearchInvestmentAdvisers_NoMatch_ReturnsNotFound()
    {
        await Seed();

        var result = await CreateTools().SearchInvestmentAdvisers("nonexistent firm");

        result.Should().Contain("No investment advisers found");
    }

    [Fact]
    public async Task SearchInvestmentAdvisers_Match_RendersTableLargestAssetsFirst()
    {
        await Seed();

        var result = await CreateTools().SearchInvestmentAdvisers("mellon");

        result.Should().Contain("BNY MELLON SECURITIES CORPORATION");
        result.Should().Contain("2,481,367,832"); // invariant grouping
        // Largest by AUM (CRD 231) renders before the smaller match (CRD 999).
        result
            .IndexOf("2,481,367,832", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("10,000,000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetInvestmentAdviser_UnknownCrd_ReturnsNotFound()
    {
        await Seed();

        var result = await CreateTools().GetInvestmentAdviser(123456);

        result.Should().Contain("No investment adviser found with CRD 123456");
    }

    [Fact]
    public async Task GetInvestmentAdviser_KnownCrd_RendersProfileWithAumAndFees()
    {
        await Seed();

        var result = await CreateTools().GetInvestmentAdviser(231);

        result.Should().Contain("BNY MELLON SECURITIES CORPORATION");
        result.Should().Contain("801-54739");
        result.Should().Contain("NEW YORK, NY, United States");
        result.Should().Contain("$2,481,367,832");
        result.Should().Contain("$829,845,109");
        result.Should().Contain("percentage of AUM");
    }
}
