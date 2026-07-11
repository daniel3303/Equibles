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
public class ShortDataToolsGetLargestShortVolumeCultureInvarianceTests : ParadeDbMcpTestBase
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

    public ShortDataToolsGetLargestShortVolumeCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetLargestShortVolume renders the Short Volume / Exempt / Total Volume cells as
    // {r.ShortVolume:N0} etc. with culture-implicit specifiers, which honour the thread
    // CurrentCulture. The established repo contract (the sibling GetShortVolume /
    // GetShortInterest culture-invariance pins and the InvariantCulture call sites: "MCP
    // markdown must not fork the separators by host locale") is byte-identical output on
    // every host. de-DE swaps the thousand separator (5,000,000 → 5.000.000), forking the
    // response — same bug class as #3013 / #3030 / #3035.
    [Fact]
    public async Task GetLargestShortVolume_UnderNonInvariantCulture_RendersShortVolumeCultureInvariantly()
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
                    Date = new DateOnly(2026, 4, 2),
                    ShortVolume = 5_000_000,
                    ShortExemptVolume = 0,
                    TotalVolume = 8_000_000,
                    Market = "ALL",
                }
            );
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetLargestShortVolume();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The volume cells (bare :N0) must render with en-US separators on every host
        // locale; de-DE would produce 5.000.000 / 8.000.000.
        result.Should().Contain("| 5,000,000 |");
        result.Should().Contain("| 8,000,000 |");
    }
}
