using System.Globalization;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CftcToolsGetCftcPositioningCultureInvarianceTests : ParadeDbMcpTestBase
{
    private CftcTools Sut() =>
        new(
            new CftcContractRepository(DbContext),
            new CftcPositionReportRepository(DbContext),
            ErrorManager,
            NullLogger<CftcTools>()
        );

    public CftcToolsGetCftcPositioningCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetCftcPositioning renders every positioning column with the culture-implicit :N0
    // specifier, which honours the thread CurrentCulture. The established repo contract
    // (the dozens of InvariantCulture call sites across the MCP tools commenting "MCP
    // markdown must not fork the separators by host locale") is that the LLM-facing
    // markdown renders the same on every host. de-DE swaps the thousand separator
    // (1,234,567 → 1.234.567), forking the response — same bug class as the fixed
    // Holdings render methods (#2628).
    [Fact(Skip = "GH-2787 — GetCftcPositioning :N0 cells follow host CurrentCulture")]
    public async Task GetCftcPositioning_UnderNonInvariantCulture_RendersOpenInterestCultureInvariantly()
    {
        var contract = new CftcContract
        {
            MarketCode = "067651",
            MarketName = "CRUDE OIL, LIGHT SWEET-WTI",
            Category = CftcContractCategory.Energy,
        };
        var report = new CftcPositionReport
        {
            CftcContract = contract,
            CftcContractId = contract.Id,
            ReportDate = new DateOnly(2026, 3, 15),
            OpenInterest = 1_234_567,
            CommLong = 600_000,
            CommShort = 400_000,
            NonCommLong = 300_000,
            NonCommShort = 250_000,
            NonCommSpreads = 50_000,
            TotalRptLong = 900_000,
            TotalRptShort = 650_000,
            NonRptLong = 50_000,
            NonRptShort = 50_000,
        };
        DbContext.Set<CftcContract>().Add(contract);
        DbContext.Set<CftcPositionReport>().Add(report);
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetCftcPositioning("067651");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // 1,234,567 open interest must render with en-US grouping on every host locale.
        result.Should().Contain("| 1,234,567 |");
    }
}
