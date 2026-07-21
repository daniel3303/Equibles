using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Congress.HostedService.Services;

[Service]
public class CongressionalTradeSyncService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CongressionalTradeSyncService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;
    private readonly CongressionalFilingLedger _filingLedger;

    public CongressionalTradeSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalTradeSyncService> logger,
        ErrorReporter errorReporter,
        CongressionalFilingLedger filingLedger
    )
    {
        _scopeFactory = scopeFactory;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _filingLedger = filingLedger;
    }

    // Congressional trade disclosures are available from 2012 (STOCK Act).
    private static readonly DateOnly EarliestAvailableDate = new(2012, 4, 1);

    // A filing with a transaction whose ticker is not (yet) a tracked stock
    // keeps re-fetching until the filing is this old, so a listing-lag gap
    // (e.g. an IPO disclosed before the stock enters CommonStock) is
    // back-matched on a later cycle. Older than this, the ticker is a
    // genuinely untracked asset and the filing is retired.
    private const int UnmatchedTickerRetryWindowDays = 30;

    public async Task SyncAll(CancellationToken ct)
    {
        var fromDate = _workerOptions.MinSyncDate.HasValue
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90));

        if (fromDate < EarliestAvailableDate)
            fromDate = EarliestAvailableDate;
        var toDate = DateOnly.FromDateTime(DateTime.UtcNow);

        _logger.LogInformation(
            "Starting congressional trade sync from {From} to {To}",
            fromDate,
            toDate
        );

        var senateProcessedIds = await _filingLedger.GetProcessedSourceIds(
            CongressionalFilingKind.SenatePeriodicTransactionReport,
            ct
        );
        var houseProcessedIds = await _filingLedger.GetProcessedSourceIds(
            CongressionalFilingKind.HousePeriodicTransactionReport,
            ct
        );

        var senateResult = await FetchDisclosureTransactions(
            "Senate",
            "CongressTrades.SyncSenate",
            sp =>
                sp.GetRequiredService<SenateDisclosureClient>()
                    .GetRecentTransactions(fromDate, toDate, senateProcessedIds, ct),
            ct
        );
        var houseResult = await FetchDisclosureTransactions(
            "House",
            "CongressTrades.SyncHouse",
            sp =>
                sp.GetRequiredService<HouseDisclosureClient>()
                    .GetRecentTransactions(fromDate, toDate, houseProcessedIds, ct),
            ct
        );

        var allTransactions = senateResult.Transactions.Concat(houseResult.Transactions).ToList();

        var outcome = TradePersistOutcome.Empty;
        if (allTransactions.Count == 0)
        {
            _logger.LogInformation("No congressional transactions found");
        }
        else
        {
            _logger.LogInformation(
                "Fetched {Count} total congressional transactions, matching to tracked stocks",
                allTransactions.Count
            );

            outcome = await ProcessTransactions(allTransactions, ct);
        }

        // Only after the transactions are committed: a failed persist above
        // throws before this point, so unrecorded filings re-fetch next cycle
        // instead of being lost.
        var unmatchedRetryCutoff = toDate.AddDays(-UnmatchedTickerRetryWindowDays);
        await _filingLedger.RecordProcessed(
            CongressionalFilingKind.SenatePeriodicTransactionReport,
            FilterRecordable(senateResult.ProcessedFilings, outcome, unmatchedRetryCutoff),
            ct
        );
        await _filingLedger.RecordProcessed(
            CongressionalFilingKind.HousePeriodicTransactionReport,
            FilterRecordable(houseResult.ProcessedFilings, outcome, unmatchedRetryCutoff),
            ct
        );
    }

    // A filing is only retired once everything it disclosed is accounted for:
    // rows that hit the member-not-found guard were parsed but never stored,
    // so their filing must keep retrying; a filing with an unmatched ticker
    // retries until it ages past the listing-lag window (see
    // UnmatchedTickerRetryWindowDays).
    internal static List<ProcessedFiling> FilterRecordable(
        List<ProcessedFiling> filings,
        TradePersistOutcome outcome,
        DateOnly unmatchedRetryCutoff
    ) =>
        filings
            .Where(f => !outcome.UnpersistedSourceIds.Contains(f.SourceId))
            .Where(f =>
                !outcome.UnmatchedTickerSourceIds.Contains(f.SourceId)
                || f.FilingDate <= unmatchedRetryCutoff
            )
            .ToList();

    /// <summary>
    /// The persistence outcome of one sync cycle: filings named here had
    /// transactions that were parsed but not stored, so they must not (yet)
    /// be recorded as ingested.
    /// </summary>
    internal sealed record TradePersistOutcome(
        IReadOnlySet<string> UnmatchedTickerSourceIds,
        IReadOnlySet<string> UnpersistedSourceIds
    )
    {
        public static readonly TradePersistOutcome Empty = new(
            new HashSet<string>(),
            new HashSet<string>()
        );
    }

    private async Task<DisclosureFetchResult> FetchDisclosureTransactions(
        string sourceLabel,
        string errorContext,
        Func<IServiceProvider, Task<DisclosureFetchResult>> fetch,
        CancellationToken ct
    )
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await fetch(scope.ServiceProvider);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Source} disclosure data", sourceLabel);
            await _errorReporter.Report(ErrorSource.CongressScraper, errorContext, ex);
            return new DisclosureFetchResult();
        }
    }

    private async Task<TradePersistOutcome> ProcessTransactions(
        List<DisclosureTransaction> transactions,
        CancellationToken ct
    )
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var memberRepository = scope.ServiceProvider.GetRequiredService<CongressMemberRepository>();
        var commonStockRepository =
            scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var stockQuery =
            _workerOptions.TickersToSync?.Count > 0
                ? commonStockRepository.GetByTickers(_workerOptions.TickersToSync)
                : commonStockRepository.GetAll();

        var stocks = await stockQuery
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Ticker, s => s, StringComparer.OrdinalIgnoreCase, ct);

        // Tickered transactions whose stock is not tracked (yet): their
        // filings stay retryable inside the listing-lag window.
        var unmatchedTickerSourceIds = transactions
            .Where(t =>
                t.SourceId != null
                && !string.IsNullOrEmpty(t.Ticker)
                && !stocks.ContainsKey(t.Ticker)
            )
            .Select(t => t.SourceId)
            .ToHashSet();

        var matched = transactions
            .Where(t => !string.IsNullOrEmpty(t.Ticker) && stocks.ContainsKey(t.Ticker))
            .ToList();

        _logger.LogInformation(
            "Matched {Matched}/{Total} transactions to tracked stocks",
            matched.Count,
            transactions.Count
        );

        if (matched.Count == 0)
            return new TradePersistOutcome(unmatchedTickerSourceIds, new HashSet<string>());

        var members = await UpsertCongressMembers(matched, dbContext, memberRepository, ct);

        // Mirrors BuildTrades' member-not-found guard: those rows are parsed
        // but never stored, so their filings must not be recorded as ingested.
        var unpersistedSourceIds = matched
            .Where(t =>
                t.SourceId != null
                && !members.ContainsKey(DisclosureParsingHelper.NormalizeMemberName(t.MemberName))
            )
            .Select(t => t.SourceId)
            .ToHashSet();

        var trades = BuildTrades(matched, members, stocks);
        await PersistTrades(trades, dbContext, ct);

        return new TradePersistOutcome(unmatchedTickerSourceIds, unpersistedSourceIds);
    }

    private async Task<Dictionary<string, CongressMember>> UpsertCongressMembers(
        List<DisclosureTransaction> matched,
        EquiblesFinancialDbContext dbContext,
        CongressMemberRepository memberRepository,
        CancellationToken ct
    )
    {
        // Key identity on the canonical name so cosmetic disclosure variants
        // (mid-name honorific, doubled first name) resolve to one record no
        // matter which scraper emitted the transaction (GH-3374). Every source
        // already normalises at emission; doing it here too makes the upsert key
        // the single source of truth for member identity.
        var distinctMembers = matched
            .GroupBy(t => DisclosureParsingHelper.NormalizeMemberName(t.MemberName))
            .Select(g => new CongressMember { Name = g.Key, Position = g.First().Position })
            .ToList();

        await dbContext
            .Set<CongressMember>()
            .UpsertRange(distinctMembers)
            .On(m => new { m.Name })
            .WhenMatched(
                (existing, incoming) => new CongressMember { Position = incoming.Position }
            )
            .RunAsync(ct);

        var memberNames = distinctMembers.Select(m => m.Name).ToList();
        return await memberRepository
            .GetAll()
            .Where(m => memberNames.Contains(m.Name))
            .ToDictionaryAsync(m => m.Name, ct);
    }

    internal List<CongressionalTrade> BuildTrades(
        List<DisclosureTransaction> matched,
        Dictionary<string, CongressMember> members,
        Dictionary<string, CommonStock> stocks
    )
    {
        var trades = new List<CongressionalTrade>();

        foreach (var tx in matched)
        {
            var memberName = DisclosureParsingHelper.NormalizeMemberName(tx.MemberName);
            if (!members.TryGetValue(memberName, out var member))
            {
                _logger.LogWarning("Congress member not found after upsert: {Name}", memberName);
                continue;
            }

            // A trade is disclosed after it happens, so the transaction date can never be after
            // the filing date. A source typo (e.g. a wrong year) that breaks this would otherwise
            // sort to the top of the member's newest-first trade history.
            if (tx.TransactionDate > tx.FilingDate)
            {
                _logger.LogWarning(
                    "Skipping congressional trade with transaction date {TransactionDate} after "
                        + "filing date {FilingDate} for {Member} ({Ticker})",
                    tx.TransactionDate,
                    tx.FilingDate,
                    tx.MemberName,
                    tx.Ticker
                );
                continue;
            }

            var stock = stocks[tx.Ticker];

            trades.Add(
                new CongressionalTrade
                {
                    CongressMemberId = member.Id,
                    CommonStockId = stock.Id,
                    TransactionDate = tx.TransactionDate,
                    FilingDate = tx.FilingDate,
                    TransactionType = tx.TransactionType,
                    // '' rather than null: OwnerType is part of the upsert key, and Postgres
                    // treats NULLs as distinct in unique indexes, which would disable dedup.
                    OwnerType = tx.OwnerType ?? "",
                    // The stored name is part of the trade upsert key (see PersistTrades), so it
                    // must be normalized here no matter which scraper emitted the transaction —
                    // an unnormalized variant would re-insert the same trade as a new row. Every
                    // source already cleans at emission; doing it here too makes this the single
                    // choke point, mirroring NormalizeMemberName above (GH-3374).
                    AssetName = DisclosureParsingHelper.CleanAssetName(tx.AssetName) ?? "",
                    AmountFrom = tx.AmountFrom,
                    AmountTo = tx.AmountTo,
                }
            );
        }

        return trades;
    }

    private async Task PersistTrades(
        List<CongressionalTrade> trades,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        // Must match the unique index on CongressionalTrade exactly (see the identity note on
        // the entity) or ON CONFLICT has no arbiter and the upsert throws. AssetName
        // participates, so dedup only works while stored names equal the current
        // CleanAssetName output — see the invariant note on CleanAssetName.
        await dbContext
            .Set<CongressionalTrade>()
            .UpsertRange(trades)
            .On(t => new
            {
                t.CommonStockId,
                t.CongressMemberId,
                t.TransactionDate,
                t.TransactionType,
                t.AssetName,
                t.OwnerType,
                t.AmountFrom,
                t.AmountTo,
            })
            .NoUpdate()
            .RunAsync(ct);

        _logger.LogInformation(
            "Upserted {Count} congressional trades (duplicates skipped)",
            trades.Count
        );
    }
}
