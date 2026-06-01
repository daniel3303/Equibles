using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsGetShortInterestSnapshotCultureInvarianceTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortInterestSnapshotCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetShortInterestSnapshot renders the Short Position cell as {r.CurrentShortPosition:N0}
    // and Days to Cover as {r.DaysToCover:F1} with culture-implicit specifiers, which honour
    // the thread CurrentCulture. The established repo contract (the sibling GetShortInterest /
    // GetShortVolume culture-invariance pins and the InvariantCulture call sites: "MCP markdown
    // must not fork the separators by host locale") is byte-identical output on every host.
    // de-DE swaps the separators (1,234,567 → 1.234.567), forking the response — same bug
    // class as #3013 / #3030.
    [Fact(
        Skip = "GH-3035 — GetShortInterestSnapshot renders Short Position / Days to Cover with host-locale separators, forking MCP output by culture"
    )]
    public async Task GetShortInterestSnapshot_UnderNonInvariantCulture_RendersShortPositionCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 15),
                    CurrentShortPosition = 1_234_567,
                    PreviousShortPosition = 1_234_567,
                    ChangeInShortPosition = 0,
                    AverageDailyVolume = 100_000,
                    DaysToCover = 12.3m,
                }
            );
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetShortInterestSnapshot();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Short Position (bare :N0) and Days to Cover (bare :F1) must render with en-US
        // separators on every host locale; de-DE would produce 1.234.567 and 12,3.
        result.Should().Contain("| 1,234,567 |");
        result.Should().Contain("| 12.3 |");
    }
}
