using System.Reflection;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Amending House PTRs replayed end to end from the real filings: the persist step and the
/// resolver against real Postgres, in both reading orders and across replays.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressionalTradeAmendmentTests : ParadeDbMcpTestBase
{
    public CongressionalTradeAmendmentTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly MethodInfo ProcessTransactionsMethod =
        typeof(CongressionalTradeSyncService).GetMethod(
            "ProcessTransactions",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    private static readonly DateOnly CastorOriginalFiled = new(2021, 7, 27);
    private static readonly DateOnly CastorAmendmentFiled = new(2021, 7, 29);
    private static readonly DateOnly HernOriginalFiled = new(2026, 8, 10);
    private static readonly DateOnly HernAmendmentFiled = new(2026, 8, 12);

    private CongressionalTradeSyncService BuildSut()
    {
        var evidenceRepository = new EquityIssuerTickerEvidenceRepository(DbContext);
        var issuerResolver = new CongressionalTradeIssuerResolver(evidenceRepository, DbContext);
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquiblesFinancialDbContext), DbContext),
            (
                typeof(ICongressMemberIdentityService),
                new CongressMemberIdentityService(
                    DbContext,
                    Substitute.For<ILogger<CongressMemberIdentityService>>()
                )
            ),
            (typeof(EquityIssuerTickerEvidenceRepository), evidenceRepository),
            (typeof(CongressionalTradeIssuerResolver), issuerResolver)
        );
        return new CongressionalTradeSyncService(
            scopeFactory,
            Options.Create(new WorkerOptions()),
            Substitute.For<ILogger<CongressionalTradeSyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<CongressionalFilingLedger>((IServiceScopeFactory)null),
            Substitute.For<CongressionalTradeImportLedger>((IServiceScopeFactory)null),
            issuerResolver,
            Substitute.For<ICongressMemberIdentityService>()
        );
    }

    private CongressionalTradeAmendmentResolver BuildResolver() =>
        new(
            DbContext,
            new ErrorReporter(
                ServiceScopeSubstitute.Create((typeof(ErrorManager), ErrorManager)),
                NullLogger<ErrorReporter>()
            ),
            NullLogger<CongressionalTradeAmendmentResolver>()
        );

    private static List<DisclosureTransaction> Read(
        string fileName,
        string member,
        string docId,
        DateOnly filingDate
    )
    {
        var transactions = HouseDisclosureClient
            .ParsePtrPdfWithShape(
                File.ReadAllBytes(
                    Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName)
                ),
                member,
                filingDate
            )
            .Transactions;
        foreach (var transaction in transactions)
        {
            transaction.SourceId = docId;
            transaction.FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport;
        }

        return transactions;
    }

    private List<DisclosureTransaction> CastorOriginal() =>
        Read("house-ptr-castor-original.pdf", "Kathy Castor", "20019168", CastorOriginalFiled);

    private List<DisclosureTransaction> CastorAmendment() =>
        Read("house-ptr-castor-amendment.pdf", "Kathy Castor", "20019181", CastorAmendmentFiled);

    private List<DisclosureTransaction> HernOriginal() =>
        Read("house-ptr-hern-original.pdf", "Kevin Hern", "20035134", HernOriginalFiled);

    private List<DisclosureTransaction> HernAmendment() =>
        Read("house-ptr-hern-amendment.pdf", "Kevin Hern", "20035196", HernAmendmentFiled);

    private async Task<CongressionalTradeSyncService.TradePersistOutcome> Persist(
        CongressionalTradeSyncService sut,
        List<DisclosureTransaction> transactions
    )
    {
        var outcome = await (Task<CongressionalTradeSyncService.TradePersistOutcome>)
            ProcessTransactionsMethod.Invoke(sut, [transactions, CancellationToken.None]);
        DbContext.ChangeTracker.Clear();
        return outcome;
    }

    private async Task Resolve()
    {
        await BuildResolver().ResolvePending(CancellationToken.None);
        DbContext.ChangeTracker.Clear();
    }

    private async Task<List<CongressionalTrade>> StoredTrades()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<CongressionalTrade>()
            .AsNoTracking()
            .OrderBy(trade => trade.SourceId)
            .ThenBy(trade => trade.SourceRowIndex)
            .ToListAsync();
    }

    [Fact]
    public async Task Resolve_CastorYearTypoAmendment_LeavesOnlyTheCorrectedTimelyRows()
    {
        var sut = BuildSut();
        await Persist(sut, CastorOriginal());
        var outcome = await Persist(sut, CastorAmendment());
        await Resolve();

        outcome.UnpersistedSourceIds.Should().BeEmpty();
        var trades = await StoredTrades();
        trades.Should().HaveCount(3);
        trades.Should().OnlyContain(trade => trade.SourceId == "20019181");
        trades.Should().OnlyContain(trade => trade.TransactionDate.Year == 2021);
        trades.Should().OnlyContain(trade => trade.FilingDate == CastorOriginalFiled);
        trades
            .Should()
            .OnlyContain(trade =>
                trade.FilingDate.DayNumber - trade.TransactionDate.DayNumber <= 45
            );

        await using var verify = Fixture.CreateDbContext();
        var records = await verify.Set<CongressionalTradeAmendment>().AsNoTracking().ToListAsync();
        records.Should().HaveCount(3);
        records
            .Should()
            .OnlyContain(row => row.Verdict == CongressionalTradeAmendmentVerdict.Matched);
        records.Select(row => row.SupersededSourceRowIndex).Should().BeEquivalentTo([0, 1, 2]);
    }

    [Fact]
    public async Task Resolve_HernAmendedAndDeletedRows_KeepsOnlyTheCorrectedOrganonRow()
    {
        var sut = BuildSut();
        await Persist(sut, HernOriginal());
        // Before v10 read the status, the deleted Versant row was stored as a trade of its own.
        DbContext.Add(
            new CongressionalTrade
            {
                CongressMemberId = (await DbContext.Set<CongressMember>().SingleAsync()).Id,
                FiledTicker = "VSNT",
                FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
                SourceId = "20035196",
                SourceRowIndex = 1,
                TransactionDate = new DateOnly(2026, 8, 5),
                FilingDate = HernAmendmentFiled,
                TransactionType = CongressTransactionType.Sale,
                OwnerType = "JT",
                AssetName = "Versant Media Group, Inc. - Class A Common Stock (VSNT)",
                AssetType = "ST",
                Subholding = "Hern Family Foundation",
                AmountFrom = 1_001,
                AmountTo = 15_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var outcome = await Persist(sut, HernAmendment());
        await Resolve();

        outcome
            .UnpersistedSourceIds.Should()
            .BeEmpty("both filings must record so their archive year can complete");
        var trades = await StoredTrades();
        trades.Should().NotContain(trade => trade.FiledTicker == "VSNT");
        var organon = trades.Should().ContainSingle(trade => trade.FiledTicker == "OGN").Subject;
        organon.SourceId.Should().Be("20035196");
        organon.Subholding.Should().Be("Hern Family Revocable Trust");
        organon.FilingDate.Should().Be(HernOriginalFiled);
        trades.Count(trade => trade.SourceId == "20035134").Should().Be(5);
    }

    [Fact]
    public async Task Persist_AmendmentReadBeforeItsOriginal_ConvergesAndSurvivesReplays()
    {
        var sut = BuildSut();
        await Persist(sut, CastorAmendment());
        await Resolve();

        await using (var verify = Fixture.CreateDbContext())
        {
            (await verify.Set<CongressionalTradeAmendment>().AsNoTracking().ToListAsync())
                .Should()
                .OnlyContain(row =>
                    row.Verdict == CongressionalTradeAmendmentVerdict.NoPredecessor
                );
        }

        await Persist(sut, CastorOriginal());
        await Resolve();
        // A parser bump reads both filings again; neither replay may bring the originals back
        // or re-date the corrected rows to the amendment.
        await Persist(sut, CastorOriginal());
        await Persist(sut, CastorAmendment());
        await Resolve();

        var trades = await StoredTrades();
        trades.Should().HaveCount(3);
        trades.Should().OnlyContain(trade => trade.SourceId == "20019181");
        trades.Should().OnlyContain(trade => trade.FilingDate == CastorOriginalFiled);
    }

    [Fact]
    public async Task Resolve_SpanbergerDescriptionOnlyAmendment_KeepsOneRow()
    {
        var sut = BuildSut();
        await Persist(
            sut,
            Read(
                "house-ptr-spanberger-original.pdf",
                "Abigail Spanberger",
                "20025205",
                new DateOnly(2024, 6, 5)
            )
        );
        await Persist(
            sut,
            Read(
                "house-ptr-spanberger-amendment.pdf",
                "Abigail Spanberger",
                "20025224",
                new DateOnly(2024, 6, 6)
            )
        );
        await Resolve();

        var trade = (await StoredTrades()).Should().ContainSingle().Subject;
        trade.SourceId.Should().Be("20025224");
        trade.FilingDate.Should().Be(new DateOnly(2024, 6, 5));
    }

    [Fact]
    public async Task Resolve_TwoFilingsExplainTheAmendment_TouchesNothingAndReportsOnce()
    {
        var sut = BuildSut();
        // The same original filed twice under two documents: either could be the one corrected.
        await Persist(sut, CastorOriginal());
        var duplicate = CastorOriginal();
        foreach (var transaction in duplicate)
            transaction.SourceId = "20019170";
        await Persist(sut, duplicate);
        await Persist(sut, CastorAmendment());
        await Resolve();
        await Resolve();

        var trades = await StoredTrades();
        trades.Should().HaveCount(9);
        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<CongressionalTradeAmendment>().AsNoTracking().ToListAsync())
            .Should()
            .OnlyContain(row => row.Verdict == CongressionalTradeAmendmentVerdict.Ambiguous);
        var error = await verify.Set<Error>().AsNoTracking().SingleAsync();
        error.Context.Should().Be(CongressionalTradeAmendmentResolver.AmbiguousErrorContext);
        error.Message.Should().Contain("20019181");
    }

    [Fact]
    public async Task Resolve_AmendmentOfAnAmendment_KeepsTheEarliestFilingDate()
    {
        var sut = BuildSut();
        await Persist(sut, CastorOriginal());
        await Persist(sut, CastorAmendment());
        await Resolve();

        // A second amendment re-files the corrected rows with only a description change.
        var second = CastorAmendment();
        foreach (var transaction in second)
            transaction.SourceId = "20019200";
        await Persist(
            sut,
            second
                .Select(transaction => new DisclosureTransaction
                {
                    SourceId = transaction.SourceId,
                    FilingKind = transaction.FilingKind,
                    SourceRowIndex = transaction.SourceRowIndex,
                    FilingStatus = transaction.FilingStatus,
                    MemberName = transaction.MemberName,
                    Position = transaction.Position,
                    Ticker = transaction.Ticker,
                    AssetName = transaction.AssetName,
                    TransactionDate = transaction.TransactionDate,
                    FilingDate = new DateOnly(2021, 8, 20),
                    TransactionType = transaction.TransactionType,
                    OwnerType = transaction.OwnerType,
                    AssetType = transaction.AssetType,
                    Subholding = transaction.Subholding,
                    AmountFrom = transaction.AmountFrom,
                    AmountTo = transaction.AmountTo,
                })
                .ToList()
        );
        await Resolve();

        var trades = await StoredTrades();
        trades.Should().HaveCount(3);
        trades.Should().OnlyContain(trade => trade.SourceId == "20019200");
        trades.Should().OnlyContain(trade => trade.FilingDate == CastorOriginalFiled);
    }

    private static List<DisclosureTransaction> Filed(
        string sourceId,
        DateOnly filingDate,
        string subholding,
        long amountFrom,
        long amountTo,
        CongressionalTradeFilingStatus status
    ) =>
        [
            new()
            {
                MemberName = "Test Member",
                Position = CongressPosition.Representative,
                Ticker = "AAPL",
                AssetName = "Apple Inc. (AAPL)",
                TransactionDate = new DateOnly(2025, 11, 3),
                FilingDate = filingDate,
                TransactionType = CongressTransactionType.Purchase,
                OwnerType = "JT",
                AssetType = "ST",
                Subholding = subholding,
                AmountFrom = amountFrom,
                AmountTo = amountTo,
                SourceId = sourceId,
                FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
                SourceRowIndex = 0,
                FilingStatus = status,
            },
        ];

    [Fact]
    public async Task Resolve_ChainReplayedNewestFirst_SupersedesBothLinksAndKeepsTheFirstDate()
    {
        // O is amended by A0 (account corrected), then A0 by A (amount corrected). A replay reads
        // newest first, so A resolves against A0 before A0 is read again.
        var original = new DateOnly(2025, 11, 20);
        var firstAmendment = new DateOnly(2025, 12, 10);
        var secondAmendment = new DateOnly(2026, 1, 15);
        List<DisclosureTransaction> O(CongressionalTradeFilingStatus status) =>
            Filed("O", original, "Brokerage", 1_001, 15_000, status);
        List<DisclosureTransaction> A0(CongressionalTradeFilingStatus status) =>
            Filed("A0", firstAmendment, "IRA", 1_001, 15_000, status);
        List<DisclosureTransaction> A(CongressionalTradeFilingStatus status) =>
            Filed("A", secondAmendment, "IRA", 15_001, 50_000, status);

        var sut = BuildSut();
        // As stored before the status was read.
        await Persist(sut, O(CongressionalTradeFilingStatus.Unstated));
        await Persist(sut, A0(CongressionalTradeFilingStatus.Unstated));
        await Persist(sut, A(CongressionalTradeFilingStatus.Unstated));

        await Persist(sut, A(CongressionalTradeFilingStatus.Amended));
        await Resolve();
        await Persist(sut, A0(CongressionalTradeFilingStatus.Amended));
        await Resolve();
        await Persist(sut, O(CongressionalTradeFilingStatus.New));
        await Resolve();

        var trade = (await StoredTrades()).Should().ContainSingle().Subject;
        trade.SourceId.Should().Be("A");
        trade.AmountFrom.Should().Be(15_001);
        trade.FilingDate.Should().Be(original);
        await using var verify = Fixture.CreateDbContext();
        var records = await verify
            .Set<CongressionalTradeAmendment>()
            .AsNoTracking()
            .ToDictionaryAsync(row => row.SourceId);
        records["A"].SupersededSourceId.Should().Be("A0");
        records["A0"].SupersededSourceId.Should().Be("O");
        records.Values.Should().OnlyContain(row => row.FirstDisclosedDate == original);
    }

    [Fact]
    public async Task Resolve_AmbiguousVerdictOnRecord_IsRetriedAndSupersedesATwin()
    {
        // An earlier matcher left this ambiguous: the original lists the trade twice, and the
        // amendment reprints one of the twins.
        var original = new DateOnly(2025, 11, 20);
        var twins = Filed(
                "O",
                original,
                "Brokerage",
                1_001,
                15_000,
                CongressionalTradeFilingStatus.New
            )
            .Concat(
                Filed("O", original, "Brokerage", 1_001, 15_000, CongressionalTradeFilingStatus.New)
            )
            .ToList();
        twins[1].SourceRowIndex = 1;
        var sut = BuildSut();
        await Persist(sut, twins);
        await Persist(
            sut,
            Filed(
                "A",
                new DateOnly(2025, 12, 10),
                "Brokerage",
                1_001,
                15_000,
                CongressionalTradeFilingStatus.Amended
            )
        );
        await DbContext
            .Set<CongressionalTradeAmendment>()
            .ExecuteUpdateAsync(
                setters =>
                    setters.SetProperty(
                        row => row.Verdict,
                        CongressionalTradeAmendmentVerdict.Ambiguous
                    ),
                CancellationToken.None
            );

        await Resolve();

        var trades = await StoredTrades();
        trades
            .Select(trade => (trade.SourceId, trade.SourceRowIndex))
            .Should()
            .Equal(("A", 0), ("O", 1));
        trades.Should().OnlyContain(trade => trade.FilingDate == original);
    }
}
