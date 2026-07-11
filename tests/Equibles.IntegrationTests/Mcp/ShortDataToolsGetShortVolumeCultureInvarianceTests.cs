using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetShortVolumeCultureInvarianceTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            new ShortSqueezeScoreManager(
                new ShortInterestRepository(DbContext),
                new DailyShortVolumeRepository(DbContext),
                new CommonStockRepository(DbContext),
                new StockSplitRepository(DbContext),
                new FailToDeliverRepository(DbContext),
                new DailyStockPriceRepository(DbContext),
                []
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortVolumeCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetShortVolume renders the Short/Exempt/Total Volume cells with the
    // culture-implicit :N0 specifier (and Short % with :F1), all of which honour
    // the thread CurrentCulture. The established repo contract (the dozens of
    // InvariantCulture call sites across the MCP tools commenting "MCP markdown
    // must not fork the separators by host locale") is that the LLM-facing
    // markdown renders byte-identically regardless of host locale. de-DE swaps the
    // thousand separator (1,234,567 → 1.234.567), forking the response — same bug
    // class as the sibling GetShortInterest cells (#2777).
    [Fact]
    public async Task GetShortVolume_UnderNonInvariantCulture_RendersVolumeCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 3, 15),
                    ShortVolume = 1_234_567,
                    ShortExemptVolume = 12_345,
                    TotalVolume = 2_000_000,
                    Market = "FNRA",
                }
            );
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut()
                .GetShortVolume("GME", startDate: "2026-01-01", endDate: "2026-04-30");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Numeric cells must render with en-US separators on any host locale:
        // volumes (:N0) and short-% (:F1). de-DE would produce 1.234.567,
        // 2.000.000, and 61,7%.
        result.Should().Contain("| 1,234,567 |");
        result.Should().Contain("| 2,000,000 |");
        result.Should().Contain("| 61.7% |");
    }
}
