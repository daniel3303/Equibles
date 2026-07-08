using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Congress.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// The unit-tier tests cover only BuildTrades and the date clamp; the
/// FlexLabs upsert path (ProcessTransactions → UpsertCongressMembers →
/// PersistTrades) needs a real Postgres and was zero-hit. Pins it end-to-end
/// via the existing scope/DB harness: a transaction whose ticker matches a
/// tracked stock upserts the member and persists the trade; an unmatched
/// ticker short-circuits before any DB write.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressionalTradeSyncServiceProcessTests : ParadeDbMcpTestBase
{
    public CongressionalTradeSyncServiceProcessTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly MethodInfo ProcessTransactionsMethod =
        typeof(CongressionalTradeSyncService).GetMethod(
            "ProcessTransactions",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    private CongressionalTradeSyncService BuildSut()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(CongressMemberRepository), new CongressMemberRepository(DbContext)),
            (typeof(CommonStockRepository), new CommonStockRepository(DbContext))
        );
        return new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    private static DisclosureTransaction Txn(
        string member,
        string ticker,
        string ownerType = "self",
        long amountFrom = 1_001,
        long amountTo = 15_000
    ) =>
        new()
        {
            MemberName = member,
            Position = CongressPosition.Senator,
            Ticker = ticker,
            AssetName = "Apple Inc.",
            TransactionDate = new DateOnly(2024, 6, 1),
            FilingDate = new DateOnly(2024, 6, 15),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = ownerType,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
        };

    [Fact]
    public async Task ProcessTransactions_TickerMatchesTrackedStock_UpsertsMemberAndPersistsTrade()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction> { Txn("Jane Doe", "AAPL") };

        await (Task)
            ProcessTransactionsMethod.Invoke(BuildSut(), [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        var member = await verify
            .Set<CongressMember>()
            .AsNoTracking()
            .SingleAsync(m => m.Name == "Jane Doe");
        member.Position.Should().Be(CongressPosition.Senator);
        var trades = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        trades.Should().ContainSingle();
        trades[0].CongressMemberId.Should().Be(member.Id);
    }

    // A member can file several same-day purchases of the same stock that differ only in the
    // amount bracket or in who holds them (self vs. a dependent child). OwnerType and the
    // amount bounds are part of the upsert unique key precisely so those are distinct trades;
    // before they were added, the second one silently collapsed into the first (NoUpdate).
    // Re-processing the same batch must still insert nothing new.
    [Fact]
    public async Task ProcessTransactions_DistinctSameDayTrades_AllPersistAndReprocessAddsNothing()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction>
        {
            Txn("Jane Doe", "AAPL"),
            Txn("Jane Doe", "AAPL", amountFrom: 15_001, amountTo: 50_000),
            Txn("Jane Doe", "AAPL", ownerType: "DC"),
        };
        var sut = BuildSut();

        await (Task)ProcessTransactionsMethod.Invoke(sut, [transactions, CancellationToken.None]);
        await (Task)ProcessTransactionsMethod.Invoke(sut, [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        var trades = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        trades.Should().HaveCount(3);
        trades
            .Select(t => (t.OwnerType, t.AmountFrom))
            .Should()
            .BeEquivalentTo([("self", 1_001L), ("self", 15_001L), ("DC", 1_001L)]);
    }

    [Fact]
    public async Task ProcessTransactions_NoTickerMatchesTrackedStock_WritesNothing()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction> { Txn("Jane Doe", "ZZZZ") };

        await (Task)
            ProcessTransactionsMethod.Invoke(BuildSut(), [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressMember>().AsNoTracking().CountAsync()).Should().Be(0);
        (await verify.Set<CongressionalTrade>().AsNoTracking().CountAsync()).Should().Be(0);
    }
}
