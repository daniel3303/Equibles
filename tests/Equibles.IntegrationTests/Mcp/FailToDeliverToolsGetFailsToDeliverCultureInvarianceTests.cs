using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FailToDeliverToolsGetFailsToDeliverCultureInvarianceTests : ParadeDbMcpTestBase
{
    private FailToDeliverTools Sut() =>
        new(
            new FailToDeliverRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FailToDeliverTools>()
        );

    public FailToDeliverToolsGetFailsToDeliverCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetFailsToDeliver renders the Quantity / Price / Value cells with the culture-implicit
    // :N0 / :F2 specifiers, which honour the thread CurrentCulture. The established repo
    // contract (the dozens of InvariantCulture call sites across the MCP tools commenting
    // "MCP markdown must not fork the separators by host locale") is that the LLM-facing
    // markdown renders the same on every host. de-DE swaps the thousand separator
    // (1,234,567 → 1.234.567), forking the response — same bug class as the fixed Holdings
    // render methods (#2628).
    [Fact]
    public async Task GetFailsToDeliver_UnderNonInvariantCulture_RendersQuantityCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        var ftd = new FailToDeliver
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            SettlementDate = new DateOnly(2026, 3, 15),
            Quantity = 1_234_567,
            Price = 25.50m,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<FailToDeliver>().Add(ftd);
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut()
                .GetFailsToDeliver("GME", startDate: "2026-01-01", endDate: "2026-12-31");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Every numeric cell must render with en-US separators on any host locale:
        // Quantity (:N0), Price (:F2), Value (:N0). de-DE would produce
        // 1.234.567 / $25,50 / $31.481.459.
        result.Should().Contain("| 1,234,567 |");
        result.Should().Contain("| $25.50 |");
        result.Should().Contain("| $31,481,459 |");
    }
}
