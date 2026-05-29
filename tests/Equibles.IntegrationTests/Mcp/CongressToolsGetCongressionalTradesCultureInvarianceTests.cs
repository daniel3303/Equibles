using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CongressToolsGetCongressionalTradesCultureInvarianceTests : ParadeDbMcpTestBase
{
    private CongressTools Sut() =>
        new(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

    public CongressToolsGetCongressionalTradesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetCongressionalTrades builds the Amount Range cell as $"${AmountFrom:N0}–${AmountTo:N0}"
    // with the culture-implicit :N0 specifier, which honours the thread CurrentCulture. The
    // established repo contract (the dozens of InvariantCulture call sites across the MCP tools
    // commenting "MCP markdown must not fork the separators by host locale") is that the
    // LLM-facing markdown renders the same on every host. de-DE swaps the thousand separator
    // (1,000,000 → 1.000.000), forking the response — same bug class as the fixed Holdings
    // render methods (#2628).
    [Fact(Skip = "GH-2785 — GetCongressionalTrades :N0 amount cells follow host CurrentCulture")]
    public async Task GetCongressionalTrades_UnderNonInvariantCulture_RendersAmountCultureInvariantly()
    {
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        var member = new CongressMember
        {
            Name = "Nancy Pelosi",
            Position = CongressPosition.Representative,
        };
        var trade = new CongressionalTrade
        {
            CongressMember = member,
            CongressMemberId = member.Id,
            CommonStock = stock,
            CommonStockId = stock.Id,
            TransactionDate = new DateOnly(2026, 3, 15),
            FilingDate = new DateOnly(2026, 4, 14),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "Self",
            AssetName = "Common Stock",
            AmountFrom = 1_000_000,
            AmountTo = 5_000_000,
        };
        DbContext.Set<CommonStock>().Add(stock);
        DbContext.Set<CongressMember>().Add(member);
        DbContext.Set<CongressionalTrade>().Add(trade);
        await DbContext.SaveChangesAsync();

        // Pin de-DE only for the rendering call; CurrentCulture flows through the
        // tool's await chain via ExecutionContext. Base class restores invariant.
        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetCongressionalTrades("NVDA");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // $1,000,000 must render with en-US grouping on every host locale.
        result.Should().Contain("$1,000,000");
    }
}
