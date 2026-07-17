using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetMemberTrades</c>'s transaction-type filter. An unrecognised
/// value must produce an explicit one-line error naming the accepted values — the previous
/// graceful degradation (ignore the filter, return every trade) made an LLM passing "banana"
/// (or any unmapped word) confidently misreport a mixed list as filtered. The natural
/// synonyms Buy/Sell must keep working as Purchase/Sale.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressToolsGetMemberTradesUnknownTransactionTypeTests : ParadeDbMcpTestBase
{
    public CongressToolsGetMemberTradesUnknownTransactionTypeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMemberTrades_UnknownTransactionType_ErrorsListingAcceptedValues()
    {
        var member = new CongressMember
        {
            Name = "Nancy Pelosi",
            Position = CongressPosition.Representative,
        };
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        DbContext.Add(member);
        DbContext.Add(stock);
        // One Purchase and one Sale on distinct dates, so a silently-ignored filter (both
        // dates rendered) is distinguishable from the expected explicit error.
        DbContext.Add(
            MakeTrade(member, stock, new DateOnly(2026, 3, 1), CongressTransactionType.Purchase)
        );
        DbContext.Add(
            MakeTrade(member, stock, new DateOnly(2026, 3, 2), CongressTransactionType.Sale)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CongressTools(
            new CongressionalTradeRepository(verify),
            new CongressMemberRepository(verify),
            new CongressionalAnnualDisclosureRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            NullLogger<CongressTools>()
        );

        var output = await sut.GetMemberTrades(
            "Nancy Pelosi",
            transactionType: "banana",
            startDate: "2026-01-01",
            endDate: "2026-12-31"
        );

        // Strict: the unknown value corrects the caller instead of silently returning
        // unfiltered trades — no trade rows, no internal error.
        output
            .Should()
            .Be(
                "Unknown transactionType 'banana'. Accepted: Purchase or Sale (synonyms: Buy, Sell)."
            );
    }

    [Fact]
    public async Task GetMemberTrades_BuySynonym_FiltersToPurchases()
    {
        var member = new CongressMember
        {
            Name = "Nancy Pelosi",
            Position = CongressPosition.Representative,
        };
        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corporation",
            Cik = "0001045810",
        };
        DbContext.Add(member);
        DbContext.Add(stock);
        DbContext.Add(
            MakeTrade(member, stock, new DateOnly(2026, 3, 1), CongressTransactionType.Purchase)
        );
        DbContext.Add(
            MakeTrade(member, stock, new DateOnly(2026, 3, 2), CongressTransactionType.Sale)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CongressTools(
            new CongressionalTradeRepository(verify),
            new CongressMemberRepository(verify),
            new CongressionalAnnualDisclosureRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            NullLogger<CongressTools>()
        );

        var output = await sut.GetMemberTrades(
            "Nancy Pelosi",
            transactionType: "Buy",
            startDate: "2026-01-01",
            endDate: "2026-12-31"
        );

        // "Buy" maps to Purchase — only the purchase row surfaces.
        output.Should().Contain("2026-03-01");
        output.Should().NotContain("2026-03-02");
    }

    private static CongressionalTrade MakeTrade(
        CongressMember member,
        CommonStock stock,
        DateOnly transactionDate,
        CongressTransactionType type
    ) =>
        new()
        {
            CongressMember = member,
            CongressMemberId = member.Id,
            CommonStock = stock,
            CommonStockId = stock.Id,
            TransactionDate = transactionDate,
            FilingDate = transactionDate.AddDays(30),
            TransactionType = type,
            OwnerType = "Self",
            AssetName = "Common Stock",
            AmountFrom = 1_000,
            AmountTo = 15_000,
        };
}
