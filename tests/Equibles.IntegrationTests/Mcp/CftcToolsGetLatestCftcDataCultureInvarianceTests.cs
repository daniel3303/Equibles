using System.Globalization;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Mcp.Tools;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CftcToolsGetLatestCftcDataCultureInvarianceTests : ParadeDbMcpTestBase
{
    private CftcTools Sut() =>
        new(
            new CftcContractRepository(DbContext),
            new CftcPositionReportRepository(DbContext),
            ErrorManager,
            NullLogger<CftcTools>()
        );

    public CftcToolsGetLatestCftcDataCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract: every MCP tool renders LLM-facing markdown the same on every host locale
    // (McpFormat.OrDash and the sibling tools all force CultureInfo.InvariantCulture so the
    // separators "do not fork by host locale"). GetLatestCftcData honours this for the date
    // and open-interest cells (via McpFormat) but renders the Comm Net / Non-Comm Net cells
    // with a bare .ToString("N0"), which follows the thread CurrentCulture — under de-DE the
    // grouping separator becomes "." (1,460,000 → 1.460.000), forking the response. Same bug
    // class as the fixed Holdings render methods (#2628).
    [Fact(
        Skip = "GH-3013 — GetLatestCftcData renders Comm Net / Non-Comm Net with host-locale separators, forking MCP output by culture"
    )]
    public async Task GetLatestCftcData_UnderNonInvariantCulture_RendersNetPositionsCultureInvariantly()
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
            OpenInterest = 9_999_999,
            CommLong = 1_500_000,
            CommShort = 40_000,
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

        // Pin de-DE only for the rendering call; CurrentCulture flows through the tool's
        // await chain via ExecutionContext. Base class restores invariant afterwards.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetLatestCftcData();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Comm Net = 1,500,000 - 40,000 = 1,460,000; Non-Comm Net = 300,000 - 250,000 = 50,000.
        // Both must render with en-US grouping on every host locale; de-DE would produce
        // 1.460.000 and 50.000.
        result.Should().Contain("1,460,000");
        result.Should().Contain("50,000");
    }
}
