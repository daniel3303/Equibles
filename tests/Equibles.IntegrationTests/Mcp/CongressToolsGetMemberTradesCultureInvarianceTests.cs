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
public class CongressToolsGetMemberTradesCultureInvarianceTests : ParadeDbMcpTestBase
{
    private CongressTools Sut() =>
        new(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CongressionalAnnualDisclosureRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

    public CongressToolsGetMemberTradesCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetMemberTrades builds the Amount Range cell as $"${AmountFrom:N0}–${AmountTo:N0}" with the
    // culture-implicit :N0 specifier, which honours the thread CurrentCulture. The established
    // repo contract (the InvariantCulture call sites and the sibling GetCongressionalTrades
    // culture pin: "MCP markdown must not fork the separators by host locale") is byte-identical
    // output on every host. de-DE swaps the thousand separator (1,000,000 → 1.000.000), forking
    // the response — same bug class as #3013 / #3030 / #3035.
    [Fact]
    public async Task GetMemberTrades_UnderNonInvariantCulture_RendersAmountCultureInvariantly()
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

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetMemberTrades("Nancy Pelosi");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Both amount-range bounds (:N0) must render with en-US grouping on every
        // host locale; de-DE would produce $1.000.000 and $5.000.000.
        result.Should().Contain("$1,000,000");
        result.Should().Contain("$5,000,000");
    }
}
