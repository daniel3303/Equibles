using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Pins <see cref="CongressionalTradeRepository.GetByStock(CommonStock, DateOnly, DateOnly)"/>:
/// the date-range overload used by the stock-detail "Congressional Trades" tab.
/// All three predicates (stock id + start + end) must be load-bearing — a
/// regression that dropped either date bound would silently leak older/newer
/// trades into the tab. The bounds are inclusive: an off-by-one drop to
/// exclusive would silently miss the boundary days, which is the most common
/// real-world failure mode.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressionalTradeRepositoryDateRangeTests : ParadeDbMcpTestBase
{
    public CongressionalTradeRepositoryDateRangeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetByStock_DateRangeInclusiveBoundaries_ReturnsTradesOnlyWithinBounds()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember
        {
            Name = "Test, Member",
            Position = CongressPosition.Senator,
        };
        DbContext.Add(stock);
        DbContext.Add(member);

        DbContext.Add(MakeTrade(stock, member, new DateOnly(2024, 11, 30))); // BEFORE start — excluded
        DbContext.Add(MakeTrade(stock, member, new DateOnly(2024, 12, 1))); // ON start — INCLUDED
        DbContext.Add(MakeTrade(stock, member, new DateOnly(2024, 12, 15))); // INSIDE — INCLUDED
        DbContext.Add(MakeTrade(stock, member, new DateOnly(2024, 12, 31))); // ON end — INCLUDED
        DbContext.Add(MakeTrade(stock, member, new DateOnly(2025, 1, 1))); // AFTER end — excluded
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var trackedStock = verify.Set<CommonStock>().Single(s => s.Ticker == "AAPL");
        var sut = new CongressionalTradeRepository(verify);

        var trades = await sut.GetByStock(
                trackedStock,
                new DateOnly(2024, 12, 1),
                new DateOnly(2024, 12, 31)
            )
            .AsNoTracking()
            .ToListAsync();

        trades.Should().HaveCount(3);
        trades
            .Select(t => t.TransactionDate)
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    new DateOnly(2024, 12, 1),
                    new DateOnly(2024, 12, 15),
                    new DateOnly(2024, 12, 31),
                }
            );
    }

    private static CongressionalTrade MakeTrade(
        CommonStock stock,
        CongressMember member,
        DateOnly transactionDate
    ) =>
        new()
        {
            CommonStock = stock,
            CongressMember = member,
            TransactionDate = transactionDate,
            FilingDate = transactionDate.AddDays(10),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "Self",
            AssetName = "Apple Inc.",
            AmountFrom = 1000,
            AmountTo = 15000,
        };
}
