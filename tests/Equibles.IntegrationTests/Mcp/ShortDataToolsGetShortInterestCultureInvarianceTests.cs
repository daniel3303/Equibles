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
public class ShortDataToolsGetShortInterestCultureInvarianceTests : ParadeDbMcpTestBase
{
    private ShortDataTools Sut() =>
        new(
            new DailyShortVolumeRepository(DbContext),
            new ShortInterestRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<ShortDataTools>()
        );

    public ShortDataToolsGetShortInterestCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // The "Short Position" cell renders {r.CurrentShortPosition:N0} with the
    // culture-implicit specifier, which honours the thread CurrentCulture. The
    // established repo contract (FormatSignedChange in this same file, and the
    // dozens of InvariantCulture call sites across the MCP tools commenting
    // "MCP markdown must not fork the separators by host locale") is that the
    // LLM-facing markdown renders byte-identically regardless of host locale.
    // de-DE swaps the thousand separator (1,234,567 → 1.234.567), forking the
    // response — same bug class as the fixed Holdings RenderTopHoldersTable (#2628).
    [Fact(
        Skip = "GH-2777 — GetShortInterest numeric cells follow host CurrentCulture (bare :N0 + McpFormat.OrDash null provider)"
    )]
    public async Task GetShortInterest_UnderNonInvariantCulture_RendersShortPositionCultureInvariantly()
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

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut()
                .GetShortInterest("GME", startDate: "2026-01-01", endDate: "2026-04-30");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // 1,234,567 shares must render with en-US grouping on every host locale.
        result.Should().Contain("| 1,234,567 |");
    }
}
