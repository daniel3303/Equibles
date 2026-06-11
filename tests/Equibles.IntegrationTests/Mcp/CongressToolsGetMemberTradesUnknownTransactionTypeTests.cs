using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetMemberTrades</c>'s transaction-type filter (the shared
/// <c>ApplyTransactionTypeFilter</c> helper). The parameter is documented as "Purchase or Sale
/// (defaults to all)", so an unrecognised value is invalid input that must degrade gracefully —
/// the filter is ignored and every trade is returned, never an internal error or an empty result.
/// Existing tests only cover exact and lowercase valid types, leaving the unparseable-input
/// fall-through branch unexercised.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressToolsGetMemberTradesUnknownTransactionTypeTests : ParadeDbMcpTestBase
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

    public CongressToolsGetMemberTradesUnknownTransactionTypeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetMemberTrades_UnknownTransactionType_IgnoresFilterAndReturnsAllTrades()
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
        // One Purchase and one Sale on distinct dates, so an applied-vs-ignored filter is visible.
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

        // An unparseable type is ignored, so both trades surface — no internal error, no empty set.
        output.Should().NotContain("An error occurred while executing");
        output.Should().Contain("2026-03-01");
        output.Should().Contain("2026-03-02");
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
