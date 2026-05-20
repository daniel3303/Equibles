using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling to <see cref="ShortDataToolsTests"/>'s positive (<c>+5,000,000</c>)
/// and negative (<c>-5,000,000</c>) change-formatting pins. Neither
/// discriminates the boundary: the just-landed <c>FormatSignedChange</c>
/// helper (#1390) uses <c>change &gt;= 0 ? "+{change:N0}" : change.ToString("N0")</c>.
/// A regression that flipped <c>&gt;=</c> to <c>&gt;</c> would silently emit
/// bare <c>"0"</c> for unchanged positions — losing the sign convention the
/// MCP table relies on. The existing zero-change test in
/// <c>GetShortInterest_NullDaysToCover_RendersEmDash</c> uses
/// <c>ChangeInShortPosition = 0</c> but only asserts on the null-fields'
/// em-dashes, not on the change column's plus sign.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortDataToolsZeroChangeTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsZeroChangeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetShortInterest_ZeroChange_RendersWithPlusSign()
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

        var result = await Sut()
            .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-04-30");

        // The pipe-delimited cell — assert positioning so we don't false-positive
        // on a stray "+0" in some other field (decimals like "12.3" can't yield it).
        result.Should().Contain("| +0 |");
    }
}
