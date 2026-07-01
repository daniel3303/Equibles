using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetLargestShortVolumeNoResultsCultureInvarianceTests
    : ParadeDbMcpTestBase
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
                new StockSplitRepository(DbContext)
            ),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetLargestShortVolumeNoResultsCultureInvarianceTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    // When no row clears the minShortVolume filter, GetLargestShortVolume returns the
    // no-results message, which renders the caller-supplied threshold as {minShortVolume:N0}
    // with the culture-implicit specifier — it honours the thread CurrentCulture. The
    // established repo contract (the InvariantCulture call sites: "MCP markdown must not fork
    // the separators by host locale") is byte-identical output on every host. de-DE swaps the
    // thousand separator (1,000,000 → 1.000.000), forking the response — same bug class as the
    // sibling data-row cells and #3013 / #3030 / #3035 / #3043 / #3047.
    [Fact]
    public async Task GetLargestShortVolume_NoResultsUnderNonInvariantCulture_RendersThresholdCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);

        // One row anchors the latest trading day but sits far below the threshold below, so the
        // minShortVolume filter empties the result set and the no-results path is exercised.
        DbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    Date = new DateOnly(2026, 4, 2),
                    ShortVolume = 500,
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
            result = await Sut().GetLargestShortVolume(minShortVolume: 1_000_000);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The threshold in the no-results message (bare :N0) must render with en-US grouping on
        // every host locale; de-DE would produce 1.000.000.
        result.Should().Contain("short volume >= 1,000,000");
    }
}
