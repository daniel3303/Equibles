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
/// tracked stock upserts the member and persists the trade; an unresolved ticker persists its
/// source fact without a company link.
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

    private CongressionalTradeSyncService BuildSut(
        ILogger<CongressionalTradeSyncService> logger = null
    )
    {
        var stocks = DbContext.Set<CommonStock>().AsNoTracking().ToList();
        foreach (var stock in stocks)
        {
            if (DbContext.Set<CommonStockTickerEvidence>().Any(row => row.CommonStockId == stock.Id))
                continue;
            DbContext.AddRange(
                Evidence(stock, new DateOnly(2020, 1, 1)),
                Evidence(stock, new DateOnly(2030, 1, 1))
            );
        }
        DbContext.SaveChanges();
        DbContext.ChangeTracker.Clear();

        var evidenceRepository = new CommonStockTickerEvidenceRepository(DbContext);
        var issuerResolver = new CongressionalTradeIssuerResolver(evidenceRepository, DbContext);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (typeof(CongressMemberRepository), new CongressMemberRepository(DbContext)),
            (typeof(CommonStockTickerEvidenceRepository), evidenceRepository),
            (typeof(CongressionalTradeIssuerResolver), issuerResolver)
        );
        return new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            logger ?? Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null),
            issuerResolver
        );
    }

    private static CommonStockTickerEvidence Evidence(CommonStock stock, DateOnly filedDate) =>
        new()
        {
            CommonStockId = stock.Id,
            Ticker = stock.Ticker,
            FiledDate = filedDate,
            SourceDocumentId = Guid.NewGuid(),
            AccessionNumber = $"{stock.Id:N}"[..24] + filedDate.Year,
        };

    private CongressionalTradeImportLedger BuildImportLedger()
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (
                typeof(CongressionalTradeImportPartitionRepository),
                new CongressionalTradeImportPartitionRepository(DbContext)
            )
        );
        return new CongressionalTradeImportLedger(scopeFactory);
    }

    private static DisclosureTransaction Txn(
        string member,
        string ticker,
        string ownerType = "self",
        long amountFrom = 1_001,
        long amountTo = 15_000,
        string assetType = "ST",
        string subholding = "",
        string assetName = "Apple Inc.",
        DateOnly? transactionDate = null,
        DateOnly? filingDate = null,
        string sourceId = null
    ) =>
        new()
        {
            MemberName = member,
            Position = CongressPosition.Senator,
            Ticker = ticker,
            AssetName = assetName,
            TransactionDate = transactionDate ?? new DateOnly(2024, 6, 1),
            FilingDate = filingDate ?? new DateOnly(2024, 6, 15),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = ownerType,
            AssetType = assetType,
            Subholding = subholding,
            AmountFrom = amountFrom,
            AmountTo = amountTo,
            SourceId = sourceId ?? $"test-{member}-{ticker}",
            FilingKind = CongressionalFilingKind.SenatePeriodicTransactionReport,
            SourceRowIndex = StableRowIndex(
                ownerType,
                amountFrom,
                amountTo,
                assetType,
                subholding,
                assetName,
                transactionDate ?? new DateOnly(2024, 6, 1)
            ),
        };

    private static int StableRowIndex(params object[] values)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in string.Join('|', values))
                hash = hash * 31 + character;
            return hash;
        }
    }

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
        trades[0].AssetType.Should().Be("ST");
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
    public async Task ProcessTransactions_SameDaySameAssetDifferentAccounts_AllPersistOnce()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction>
        {
            Txn("Jane Doe", "AAPL", assetType: "ST", subholding: "Brokerage Account"),
            Txn("Jane Doe", "AAPL", assetType: "ST", subholding: "Retirement Account"),
            Txn("Jane Doe", "AAPL", assetType: "OP", subholding: "Brokerage Account"),
        };
        var sut = BuildSut();

        await (Task)ProcessTransactionsMethod.Invoke(sut, [transactions, CancellationToken.None]);
        await (Task)ProcessTransactionsMethod.Invoke(sut, [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        var trades = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        trades.Should().HaveCount(3);
        trades
            .Select(t => (t.AssetType, t.Subholding))
            .Should()
            .BeEquivalentTo([
                ("ST", "Brokerage Account"),
                ("ST", "Retirement Account"),
                ("OP", "Brokerage Account"),
            ]);
    }

    [Fact]
    public async Task ProcessTransactions_NoIssuerEvidence_PersistsUnlinkedSourceFact()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction> { Txn("Jane Doe", "ZZZZ") };

        await (Task)
            ProcessTransactionsMethod.Invoke(BuildSut(), [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressMember>().AsNoTracking().CountAsync()).Should().Be(1);
        var trade = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        trade.CommonStockId.Should().BeNull();
        trade.FiledTicker.Should().Be("ZZZZ");
    }

    [Fact]
    public async Task ProcessTransactions_StableSourceRowChangesTicker_RefusesReplayAndKeepsFiledFact()
    {
        DbContext.AddRange(
            new CommonStock { Ticker = "AAPL", Name = "Apple Inc." },
            new CommonStock { Ticker = "MSFT", Name = "Microsoft Corporation" }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var sut = BuildSut();
        var original = Txn("Jane Doe", "AAPL", sourceId: "immutable-ticker-filing");

        await (Task)
            ProcessTransactionsMethod.Invoke(
                sut,
                [new List<DisclosureTransaction> { original }, CancellationToken.None]
            );
        var replay = Txn("Jane Doe", "MSFT", sourceId: "immutable-ticker-filing");
        var replayTask = (Task)
            ProcessTransactionsMethod.Invoke(
                sut,
                [new List<DisclosureTransaction> { replay }, CancellationToken.None]
            );
        await replayTask;

        var outcome = replayTask.GetType().GetProperty("Result")!.GetValue(replayTask)!;
        var unpersisted =
            (IEnumerable<string>)
                outcome.GetType().GetProperty("UnpersistedSourceIds")!.GetValue(outcome)!;
        unpersisted.Should().Contain("immutable-ticker-filing");

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify
            .Set<CongressionalTrade>()
            .AsNoTracking()
            .Include(trade => trade.CommonStock)
            .SingleAsync();
        stored.FiledTicker.Should().Be("AAPL");
        stored.CommonStock.Ticker.Should().Be("AAPL");
    }

    [Fact]
    public async Task CommonStockDeletion_PreservesFiledTradeAndClearsDerivedIssuerLink()
    {
        var stock = new CommonStock { Ticker = "ACME", Name = "Acme Corporation" };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CommonStock = stock,
                FiledTicker = "ACME",
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Acme Corporation",
                AssetType = "ST",
                Subholding = "",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        DbContext.Remove(await DbContext.Set<CommonStock>().SingleAsync(row => row.Id == stock.Id));
        await DbContext.SaveChangesAsync();

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        stored.CommonStockId.Should().BeNull();
        stored.FiledTicker.Should().Be("ACME");
    }

    [Fact]
    public async Task RelinkUnresolved_NewBracketingEvidence_LinksStoredSourceFactLater()
    {
        var issuer = new CommonStock { Ticker = "ACME", Name = "Acme Corporation" };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        var trade = new CongressionalTrade
        {
            CongressMember = member,
            FiledTicker = "ACME",
            TransactionDate = new DateOnly(2024, 5, 1),
            FilingDate = new DateOnly(2024, 5, 10),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "self",
            AssetName = "Acme Corporation",
            AssetType = "ST",
            Subholding = "",
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };
        DbContext.AddRange(issuer, member, trade);
        DbContext.Add(Evidence(issuer, new DateOnly(2024, 2, 1)));
        await DbContext.SaveChangesAsync();

        var resolver = new CongressionalTradeIssuerResolver(
            new CommonStockTickerEvidenceRepository(DbContext),
            DbContext
        );
        (await resolver.RelinkUnresolved(CancellationToken.None)).Should().Be(0);

        DbContext.Add(Evidence(issuer, new DateOnly(2024, 8, 1)));
        await DbContext.SaveChangesAsync();

        (await resolver.RelinkUnresolved(CancellationToken.None)).Should().Be(1);
        trade.CommonStockId.Should().Be(issuer.Id);
    }

    [Fact]
    public async Task ProcessTransactions_ReusedTickerReplay_RelinksLegacyRowToHistoricalIssuer()
    {
        var historicalIssuer = new CommonStock { Ticker = "B", Name = "Barrick Gold" };
        var currentIssuer = new CommonStock { Ticker = "GOLD", Name = "Gold.com" };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(historicalIssuer, currentIssuer, member);
        DbContext.AddRange(
            new CommonStockTickerEvidence
            {
                CommonStock = historicalIssuer,
                Ticker = "GOLD",
                FiledDate = new DateOnly(2020, 2, 1),
                SourceDocumentId = Guid.NewGuid(),
                AccessionNumber = "historical-before",
            },
            new CommonStockTickerEvidence
            {
                CommonStock = historicalIssuer,
                Ticker = "GOLD",
                FiledDate = new DateOnly(2022, 2, 1),
                SourceDocumentId = Guid.NewGuid(),
                AccessionNumber = "historical-after",
            },
            new CommonStockTickerEvidence
            {
                CommonStock = currentIssuer,
                Ticker = "GOLD",
                FiledDate = new DateOnly(2025, 2, 1),
                SourceDocumentId = Guid.NewGuid(),
                AccessionNumber = "current-before",
            },
            new CongressionalTrade
            {
                CongressMember = member,
                CommonStock = currentIssuer,
                TransactionDate = new DateOnly(2021, 6, 1),
                FilingDate = new DateOnly(2021, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Barrick Gold Corporation",
                AssetType = "ST",
                Subholding = "",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var source = Txn(
            "Jane Doe",
            "GOLD",
            assetName: "Barrick Gold Corporation",
            transactionDate: new DateOnly(2021, 6, 1),
            filingDate: new DateOnly(2021, 6, 15),
            sourceId: "house-4304"
        );

        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [new List<DisclosureTransaction> { source }, CancellationToken.None]
            );

        await using var verify = Fixture.CreateDbContext();
        var corrected = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        corrected.CommonStockId.Should().Be(historicalIssuer.Id);
        corrected.FiledTicker.Should().Be("GOLD");
        corrected.SourceId.Should().Be("house-4304");
        corrected.SourceRowIndex.Should().Be(source.SourceRowIndex);
    }

    [Fact]
    public async Task ProcessTransactions_TransactionAfterFiling_KeepsSourceUnpersisted()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var transaction = Txn(
            "Jane Doe",
            "AAPL",
            transactionDate: new DateOnly(2024, 6, 16),
            filingDate: new DateOnly(2024, 6, 15),
            sourceId: "future-date-filing"
        );

        var processTask = (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [new List<DisclosureTransaction> { transaction }, CancellationToken.None]
            );
        await processTask;
        var outcome = processTask.GetType().GetProperty("Result")!.GetValue(processTask)!;
        var unpersisted =
            (IEnumerable<string>)
                outcome.GetType().GetProperty("UnpersistedSourceIds")!.GetValue(outcome)!;

        unpersisted.Should().Contain("future-date-filing");
        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressionalTrade>().AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessTransactions_UnmatchedFutureTransaction_KeepsSourceUnpersisted()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var transaction = Txn(
            "Jane Doe",
            null,
            transactionDate: new DateOnly(2024, 6, 16),
            filingDate: new DateOnly(2024, 6, 15),
            sourceId: "tickerless-future-date-filing"
        );

        var processTask = (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [new List<DisclosureTransaction> { transaction }, CancellationToken.None]
            );
        await processTask;
        var outcome = processTask.GetType().GetProperty("Result")!.GetValue(processTask)!;
        var unpersisted =
            (IEnumerable<string>)
                outcome.GetType().GetProperty("UnpersistedSourceIds")!.GetValue(outcome)!;

        unpersisted.Should().Contain("tickerless-future-date-filing");
    }

    [Fact]
    public async Task ProcessTransactions_ReplayedRangeFloor_RepairsLegacyRowAndRetainsFiledMetadata()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 0,
                AmountTo = 15_001,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction>
        {
            Txn(
                "Jane Doe",
                "AAPL",
                amountFrom: 15_001,
                amountTo: 50_000,
                assetType: "OP",
                subholding: "Brokerage IRA"
            ),
        };

        await (Task)
            ProcessTransactionsMethod.Invoke(BuildSut(), [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        var repaired = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        repaired.AmountFrom.Should().Be(15_001);
        repaired.AmountTo.Should().Be(50_000);
        repaired.AssetType.Should().Be("OP");
        repaired.Subholding.Should().Be("Brokerage IRA");
    }

    [Fact]
    public async Task ProcessTransactions_ReplayedExistingTrade_EnrichesEmptyFiledMetadata()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction>
        {
            Txn("Jane Doe", "AAPL", assetType: "OP", subholding: "Brokerage IRA"),
        };

        await (Task)
            ProcessTransactionsMethod.Invoke(BuildSut(), [transactions, CancellationToken.None]);

        await using var verify = Fixture.CreateDbContext();
        var enriched = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        enriched.AssetType.Should().Be("OP");
        enriched.Subholding.Should().Be("Brokerage IRA");
    }

    [Fact]
    public async Task ProcessTransactions_RedatedDuplicateInlineSubholdingReplay_ReplacesPollutedLegacyName()
    {
        const string subholding =
            "150 Main Street Trust > Pershing Advisor Solutions LLC Brokerage";
        const string otherSubholding = "Different Brokerage Account";
        var stock = new CommonStock { Ticker = "AVGO", Name = "Broadcom Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 14),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName =
                    $"Broadcom Inc. (AVGO) F S: New S O: {subholding} D: Put option, strike price",
                AssetType = "OP",
                Subholding = "",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn(
                            "Jane Doe",
                            "AVGO",
                            assetType: "OP",
                            subholding: otherSubholding,
                            assetName: "Broadcom Inc. (AVGO)"
                        ),
                        Txn(
                            "Jane Doe",
                            "AVGO",
                            assetType: "ST",
                            subholding: subholding,
                            assetName: "Broadcom Inc. (AVGO)"
                        ),
                        Txn(
                            "Jane Doe",
                            "AVGO",
                            assetType: "OP",
                            subholding: subholding,
                            assetName: "Broadcom Inc. (AVGO)"
                        ),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        stored.Should().HaveCount(3);
        var repaired = stored.Single(trade =>
            trade.AssetType == "OP" && trade.Subholding == subholding
        );
        repaired.AssetName.Should().Be("Broadcom Inc. (AVGO)");
    }

    [Fact]
    public async Task ProcessTransactions_TwoPollutedLegacyAccounts_ReplaySeparatesBothAccounts()
    {
        const string firstSubholding = "Brokerage Account A";
        const string secondSubholding = "Brokerage Account B";
        var stock = new CommonStock { Ticker = "AVGO", Name = "Broadcom Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.AddRange(LegacyTrade(firstSubholding), LegacyTrade(secondSubholding));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn(
                            "Jane Doe",
                            "AVGO",
                            assetType: "OP",
                            subholding: firstSubholding,
                            assetName: "Broadcom Inc. (AVGO)"
                        ),
                        Txn(
                            "Jane Doe",
                            "AVGO",
                            assetType: "OP",
                            subholding: secondSubholding,
                            assetName: "Broadcom Inc. (AVGO)"
                        ),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var repaired = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        repaired.Should().HaveCount(2);
        repaired.Should().OnlyContain(trade => trade.AssetName == "Broadcom Inc. (AVGO)");
        repaired
            .Select(trade => trade.Subholding)
            .Should()
            .BeEquivalentTo(firstSubholding, secondSubholding);

        CongressionalTrade LegacyTrade(string subholding) =>
            new()
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 14),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName =
                    $"Broadcom Inc. (AVGO) F S: New S O: {subholding} D: Put option, strike price",
                AssetType = "OP",
                Subholding = "",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            };
    }

    [Fact]
    public async Task ProcessTransactions_DifferentAccountArrivesLater_PersistsBothAccounts()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var sut = BuildSut();

        await (Task)
            ProcessTransactionsMethod.Invoke(
                sut,
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", subholding: "Account A"),
                    },
                    CancellationToken.None,
                ]
            );
        await (Task)
            ProcessTransactionsMethod.Invoke(
                sut,
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", subholding: "Account B"),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        stored.Should().HaveCount(2);
        stored.Select(trade => trade.Subholding).Should().BeEquivalentTo("Account A", "Account B");
    }

    [Fact]
    public async Task ProcessTransactions_PartialStoredMetadata_PreservesBothFiledIdentities()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AssetType = "ST",
                Subholding = "",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", assetType: "OP", subholding: "Brokerage IRA"),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify.Set<CongressionalTrade>().AsNoTracking().ToListAsync();
        stored.Should().HaveCount(2);
        stored
            .Select(trade => (trade.AssetType, trade.Subholding))
            .Should()
            .BeEquivalentTo(new[] { ("ST", ""), ("OP", "Brokerage IRA") });
    }

    [Fact]
    public async Task ProcessTransactions_SameCycleEmptyMetadataDuplicate_PrefersFiledMetadata()
    {
        DbContext.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", assetType: "", subholding: ""),
                        Txn("Jane Doe", "AAPL", assetType: "OP", subholding: "Brokerage IRA"),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        stored.AssetType.Should().Be("OP");
        stored.Subholding.Should().Be("Brokerage IRA");
    }

    [Fact]
    public async Task ProcessTransactions_ReplayedUnderAmount_RepairsLegacyZeroFloorRow()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 0,
                AmountTo = 1_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", amountFrom: 1, amountTo: 1_000),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var repaired = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        repaired.AmountFrom.Should().Be(1);
        repaired.AmountTo.Should().Be(1_000);
    }

    [Fact]
    public async Task ProcessTransactions_TwoReplayedRangeFloors_RepairsBothLegacyRows()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.AddRange(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 0,
                AmountTo = 15_001,
            },
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 0,
                AmountTo = 50_001,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await (Task)
            ProcessTransactionsMethod.Invoke(
                BuildSut(),
                [
                    new List<DisclosureTransaction>
                    {
                        Txn("Jane Doe", "AAPL", amountFrom: 15_001, amountTo: 50_000),
                        Txn("Jane Doe", "AAPL", amountFrom: 50_001, amountTo: 100_000),
                    },
                    CancellationToken.None,
                ]
            );

        await using var verify = Fixture.CreateDbContext();
        var stored = await verify
            .Set<CongressionalTrade>()
            .AsNoTracking()
            .OrderBy(t => t.AmountFrom)
            .ToListAsync();
        stored
            .Select(t => (t.AmountFrom, t.AmountTo))
            .Should()
            .Equal((15_001, 50_000), (50_001, 100_000));
    }

    [Fact]
    public async Task ProcessTransactions_ReplacementUpsertFails_RollsBackLegacyRepairDeletion()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var member = new CongressMember { Name = "Jane Doe", Position = CongressPosition.Senator };
        DbContext.AddRange(stock, member);
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMember = member,
                CongressMemberId = member.Id,
                CommonStock = stock,
                CommonStockId = stock.Id,
                TransactionDate = new DateOnly(2024, 6, 1),
                FilingDate = new DateOnly(2024, 6, 15),
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "self",
                AssetName = "Apple Inc.",
                AmountFrom = 0,
                AmountTo = 15_001,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var transactions = new List<DisclosureTransaction>
        {
            Txn(
                "Jane Doe",
                "AAPL",
                amountFrom: 15_001,
                amountTo: 50_000,
                assetType: new string('X', 129)
            ),
        };

        var act = async () =>
            await (Task)
                ProcessTransactionsMethod.Invoke(
                    BuildSut(),
                    [transactions, CancellationToken.None]
                );

        await act.Should().ThrowAsync<Exception>();

        await using var verify = Fixture.CreateDbContext();
        var legacy = await verify.Set<CongressionalTrade>().AsNoTracking().SingleAsync();
        legacy.AmountFrom.Should().Be(0);
        legacy.AmountTo.Should().Be(15_001);
    }

    [Fact]
    public async Task ImportLedger_NewerParserVersion_ReopensCompletedArchiveYear()
    {
        var ledger = BuildImportLedger();
        var kind = CongressionalFilingKind.SenatePeriodicTransactionReport;
        await ledger.RecordCompleted(kind, 2018, 1, 12, 34, CancellationToken.None);

        var currentParserYear = await ledger.GetNextYear(
            kind,
            1,
            2018,
            2018,
            CancellationToken.None
        );
        var newerParserYear = await ledger.GetNextYear(kind, 2, 2018, 2018, CancellationToken.None);

        currentParserYear.Should().BeNull();
        newerParserYear.Should().Be(2018);
    }
}
