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
    private readonly CongressionalTradeImportLedger _importLedger;

    public CongressionalTradeSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalTradeSyncService> logger,
        ErrorReporter errorReporter,
        CongressionalFilingLedger filingLedger,
        CongressionalTradeImportLedger importLedger
    )
    {
        _scopeFactory = scopeFactory;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _filingLedger = filingLedger;
        _importLedger = importLedger;
    }

    // Congressional trade disclosures are available from 2012 (STOCK Act).
    private static readonly DateOnly EarliestAvailableDate = new(2012, 4, 1);
    private const int TradeParserVersion = 3;
    private const int ReprocessPerCycleLimit = 1_000;

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
            : new DateOnly(DateTime.UtcNow.Year, 1, 1);

        if (fromDate < EarliestAvailableDate)
            fromDate = EarliestAvailableDate;
        var toDate = DateOnly.FromDateTime(DateTime.UtcNow);

        _logger.LogInformation(
            "Starting congressional trade sync from {From} to {To}",
            fromDate,
            toDate
        );

        var batches = await FetchBatches(fromDate, toDate, ct);
        var allTransactions = batches.SelectMany(b => b.Result.Transactions).ToList();

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
        foreach (var batch in batches)
            await RecordBatch(batch, outcome, unmatchedRetryCutoff, ct);
    }

    private async Task<List<TradeFetchBatch>> FetchBatches(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct
    )
    {
        var batches = new List<TradeFetchBatch>();
        var archiveCandidates = new List<(CongressionalFilingKind Kind, int Year)>();
        foreach (var kind in PeriodicTransactionKinds)
        {
            var processed = await _filingLedger.GetProcessedSourceIds(
                kind,
                ct,
                TradeParserVersion,
                ReprocessPerCycleLimit
            );
            var current = await FetchRange(kind, fromDate, toDate, processed, ct);
            batches.Add(new TradeFetchBatch(kind, current, null));

            var archiveYear = await _importLedger.GetNextYear(
                kind,
                TradeParserVersion,
                EarliestAvailableDate.Year,
                fromDate.Year - 1,
                ct
            );
            if (archiveYear != null)
                archiveCandidates.Add((kind, archiveYear.Value));
        }

        // Bound historical work to one chamber/year partition per cycle. Prefer the newest
        // missing year across both chambers, then the stable chamber order above for ties.
        var archiveCandidate = archiveCandidates
            .OrderByDescending(candidate => candidate.Year)
            .ThenBy(candidate => Array.IndexOf(PeriodicTransactionKinds, candidate.Kind))
            .FirstOrDefault();
        if (archiveCandidate != default)
        {
            // Only current-parser ledger rows are skipped in the bounded archive window. Every
            // stale filing in that one year is replayed, while later years remain untouched.
            var archiveProcessed = await _filingLedger.GetProcessedSourceIds(
                archiveCandidate.Kind,
                ct,
                TradeParserVersion,
                int.MaxValue
            );
            var archiveStart = new DateOnly(archiveCandidate.Year, 1, 1);
            if (archiveStart < EarliestAvailableDate)
                archiveStart = EarliestAvailableDate;
            var archiveEnd = new DateOnly(archiveCandidate.Year, 12, 31);
            var archive = await FetchRange(
                archiveCandidate.Kind,
                archiveStart,
                archiveEnd,
                archiveProcessed,
                ct
            );
            batches.Add(new TradeFetchBatch(archiveCandidate.Kind, archive, archiveCandidate.Year));
        }

        return batches;
    }

    private Task<DisclosureFetchResult> FetchRange(
        CongressionalFilingKind kind,
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlySet<string> processed,
        CancellationToken ct
    ) =>
        kind switch
        {
            CongressionalFilingKind.SenatePeriodicTransactionReport => FetchDisclosureTransactions(
                "Senate",
                "CongressTrades.SyncSenate",
                sp =>
                    sp.GetRequiredService<SenateDisclosureClient>()
                        .GetRecentTransactions(fromDate, toDate, processed, ct),
                ct
            ),
            CongressionalFilingKind.HousePeriodicTransactionReport => FetchDisclosureTransactions(
                "House",
                "CongressTrades.SyncHouse",
                sp =>
                    sp.GetRequiredService<HouseDisclosureClient>()
                        .GetRecentTransactions(fromDate, toDate, processed, ct),
                ct
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

    private async Task RecordBatch(
        TradeFetchBatch batch,
        TradePersistOutcome outcome,
        DateOnly unmatchedRetryCutoff,
        CancellationToken ct
    )
    {
        var recordable = FilterRecordable(
            batch.Result.ProcessedFilings,
            outcome,
            unmatchedRetryCutoff
        );
        await _filingLedger.RecordProcessed(batch.Kind, recordable, ct, TradeParserVersion);

        if (
            batch.ArchiveYear == null
            || !batch.Result.IsComplete
            || recordable.Count != batch.Result.ProcessedFilings.Count
        )
            return;

        await _importLedger.RecordCompleted(
            batch.Kind,
            batch.ArchiveYear.Value,
            TradeParserVersion,
            recordable.Count,
            batch.Result.Transactions.Count,
            ct
        );
    }

    private static readonly CongressionalFilingKind[] PeriodicTransactionKinds =
    [
        CongressionalFilingKind.SenatePeriodicTransactionReport,
        CongressionalFilingKind.HousePeriodicTransactionReport,
    ];

    private sealed record TradeFetchBatch(
        CongressionalFilingKind Kind,
        DisclosureFetchResult Result,
        int? ArchiveYear
    );

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
            return new DisclosureFetchResult { IsComplete = false };
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

        // A source row dated after its own filing is never recordable, even when the ticker is
        // absent or not tracked. Keep the whole filing retryable rather than checkpointing a
        // semantically malformed disclosure.
        var unpersistedSourceIds = transactions
            .Where(t => t.SourceId != null && t.TransactionDate > t.FilingDate)
            .Select(t => t.SourceId)
            .ToHashSet();

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
            return new TradePersistOutcome(unmatchedTickerSourceIds, unpersistedSourceIds);

        var members = await UpsertCongressMembers(matched, dbContext, memberRepository, ct);

        // Mirrors BuildTrades' member-not-found guard: those rows are parsed
        // but never stored, so their filings must not be recorded as ingested.
        unpersistedSourceIds.UnionWith(
            matched
                .Where(t =>
                    t.SourceId != null
                    && !members.ContainsKey(
                        DisclosureParsingHelper.NormalizeMemberName(t.MemberName)
                    )
                )
                .Select(t => t.SourceId)
        );

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
            .Select(g => new CongressMember
            {
                Name = g.Key,
                Position = g.First().Position,
                StateDistrict = SelectStateDistrict(g),
            })
            .ToList();

        await dbContext
            .Set<CongressMember>()
            .UpsertRange(distinctMembers)
            .On(m => new { m.Name })
            .WhenMatched(
                (existing, incoming) =>
                    new CongressMember
                    {
                        Position = incoming.Position,
                        // Coalesced, never overwritten with nothing: this lane sees the same
                        // member through Senate transactions too, and those state no seat. A
                        // straight assignment would wipe a recorded seat on the next cycle.
                        StateDistrict = incoming.StateDistrict ?? existing.StateDistrict,
                    }
            )
            .RunAsync(ct);

        var memberNames = distinctMembers.Select(m => m.Name).ToList();
        return await memberRepository
            .GetAll()
            .Where(m => memberNames.Contains(m.Name))
            .ToDictionaryAsync(m => m.Name, ct);
    }

    /// <summary>
    /// The seat to record for a member from this batch of transactions. Only the House index
    /// states one, so a member's rows mix seat-bearing and seat-less transactions and picking
    /// the wrong one would blank a recorded seat. Redistricting moves a member between
    /// districts, so the most recently filed transaction that states a seat wins.
    /// </summary>
    internal static string SelectStateDistrict(IEnumerable<DisclosureTransaction> transactions) =>
        transactions
            .Where(t => !string.IsNullOrWhiteSpace(t.StateDistrict))
            .OrderBy(t => t.FilingDate)
            .LastOrDefault()
            ?.StateDistrict.Trim();

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
                    OwnerType = CleanStoredText(tx.OwnerType),
                    // The stored name is part of the trade upsert key (see PersistTrades), so it
                    // must be normalized here no matter which scraper emitted the transaction —
                    // an unnormalized variant would re-insert the same trade as a new row. Every
                    // source already cleans at emission; doing it here too makes this the single
                    // choke point, mirroring NormalizeMemberName above (GH-3374).
                    AssetName = DisclosureParsingHelper.CleanAssetName(
                        CleanStoredText(tx.AssetName)
                    ),
                    AssetType = CleanStoredText(tx.AssetType),
                    Subholding = CleanStoredText(tx.Subholding),
                    AmountFrom = tx.AmountFrom,
                    AmountTo = tx.AmountTo,
                }
            );
        }

        return trades;
    }

    private static string CleanStoredText(string value) =>
        value?.Replace("\0", "", StringComparison.Ordinal).Trim() ?? "";

    private async Task RepairLegacyInlineSubholdingNames(
        List<CongressionalTrade> incoming,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        if (incoming.Count == 0)
            return;

        var incomingByIdentity = incoming
            .GroupBy(InlineMetadataRepairIdentity.From)
            .ToDictionary(group => group.Key, group => group.ToList());
        var stockIds = incoming.Select(t => t.CommonStockId).Distinct().ToList();
        var memberIds = incoming.Select(t => t.CongressMemberId).Distinct().ToList();
        var firstDate = incoming.Min(t => t.TransactionDate);
        var lastDate = incoming.Max(t => t.TransactionDate);
        var legacyRows = await dbContext
            .Set<CongressionalTrade>()
            .Where(t =>
                stockIds.Contains(t.CommonStockId)
                && memberIds.Contains(t.CongressMemberId)
                && t.TransactionDate >= firstDate
                && t.TransactionDate <= lastDate
                && t.AssetName.Contains(":")
            )
            .ToListAsync(ct);
        var legacyCandidates = new List<LegacyInlineMetadataCandidate>();
        foreach (var legacy in legacyRows)
        {
            var details = HouseDisclosureClient.ExtractInlineAssetDetails(legacy.AssetName);
            if (details.Subholding == null)
                continue;

            var cleanedName = DisclosureParsingHelper.CleanAssetName(details.AssetName);
            var cleanedSubholding = CleanStoredText(
                DisclosureParsingHelper.Truncate(details.Subholding, 256)
            );
            legacyCandidates.Add(
                new LegacyInlineMetadataCandidate(
                    legacy,
                    InlineMetadataRepairIdentity.From(legacy, cleanedName),
                    cleanedSubholding
                )
            );
        }

        var repairs = new List<CongressionalTrade>();
        foreach (var legacyCandidate in legacyCandidates)
        {
            if (!incomingByIdentity.TryGetValue(legacyCandidate.Identity, out var candidates))
                continue;
            var legacy = legacyCandidate.Trade;
            if (legacy.Subholding != "" && legacy.Subholding != legacyCandidate.CleanedSubholding)
            {
                _logger.LogWarning(
                    "Deferred congressional trade inline-metadata repair because stored and filed subholdings conflict"
                );
                continue;
            }

            var matchingSubholding = candidates
                .Where(candidate => candidate.Subholding == legacyCandidate.CleanedSubholding)
                .ToList();
            List<CongressionalTrade> selectedCandidates;
            if (!string.IsNullOrEmpty(legacy.AssetType))
            {
                selectedCandidates = matchingSubholding
                    .Where(candidate => candidate.AssetType == legacy.AssetType)
                    .ToList();
            }
            else
            {
                var candidateAssetTypes = matchingSubholding
                    .Select(candidate => candidate.AssetType)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                selectedCandidates = candidateAssetTypes.Count == 1 ? matchingSubholding : [];
            }

            if (selectedCandidates.Count == 0)
            {
                _logger.LogWarning(
                    "Deferred congressional trade inline-metadata repair because replay did not supply one authoritative account and asset type"
                );
                continue;
            }

            repairs.Add(legacy);
        }

        if (repairs.Count == 0)
            return;

        dbContext.RemoveRange(repairs);
        await dbContext.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Removed {Count} legacy congressional trades whose inline House metadata was separated by source replay",
            repairs.Count
        );
    }

    private async Task PersistTrades(
        List<CongressionalTrade> trades,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        if (trades.Count == 0)
            return;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await RepairLegacyInlineSubholdingNames(trades, dbContext, ct);
        await RepairLegacyEmptyFiledMetadata(trades, dbContext, ct);
        await RepairLegacyZeroFloorAmounts(trades, dbContext, ct);
        RemoveUnidentifiedMetadataDuplicates(trades);

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
                t.AssetType,
                t.Subholding,
            })
            .NoUpdate()
            .RunAsync(ct);

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Upserted {Count} congressional trades (duplicates skipped)",
            trades.Count
        );
    }

    private async Task RepairLegacyEmptyFiledMetadata(
        IReadOnlyList<CongressionalTrade> incoming,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        var authoritativeIdentities = incoming
            .Where(HasFiledMetadata)
            .Select(InlineMetadataRepairIdentity.From)
            .ToHashSet();
        if (authoritativeIdentities.Count == 0)
            return;

        var stockIds = incoming.Select(t => t.CommonStockId).Distinct().ToList();
        var memberIds = incoming.Select(t => t.CongressMemberId).Distinct().ToList();
        var firstDate = incoming.Min(t => t.TransactionDate);
        var lastDate = incoming.Max(t => t.TransactionDate);
        var legacyRows = await dbContext
            .Set<CongressionalTrade>()
            .Where(t =>
                stockIds.Contains(t.CommonStockId)
                && memberIds.Contains(t.CongressMemberId)
                && t.TransactionDate >= firstDate
                && t.TransactionDate <= lastDate
                && t.AssetType == ""
                && t.Subholding == ""
            )
            .ToListAsync(ct);
        var repairs = legacyRows
            .Where(legacy =>
                authoritativeIdentities.Contains(InlineMetadataRepairIdentity.From(legacy))
            )
            .ToList();
        if (repairs.Count == 0)
            return;

        dbContext.RemoveRange(repairs);
        await dbContext.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Removed {Count} legacy congressional trades whose empty filed metadata was supplied by source replay",
            repairs.Count
        );
    }

    private static void RemoveUnidentifiedMetadataDuplicates(List<CongressionalTrade> trades)
    {
        var authoritativeIdentities = trades
            .Where(HasFiledMetadata)
            .Select(InlineMetadataRepairIdentity.From)
            .ToHashSet();
        trades.RemoveAll(trade =>
            !HasFiledMetadata(trade)
            && authoritativeIdentities.Contains(InlineMetadataRepairIdentity.From(trade))
        );
    }

    private static bool HasFiledMetadata(CongressionalTrade trade) =>
        !string.IsNullOrEmpty(trade.AssetType) || !string.IsNullOrEmpty(trade.Subholding);

    private async Task RepairLegacyZeroFloorAmounts(
        List<CongressionalTrade> incoming,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        var incomingByBase = incoming
            .Where(t => t.AmountFrom > 0)
            .GroupBy(TradeBaseIdentity.From)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(t => new TradeAmountRange(t.AmountFrom, t.AmountTo, t.FilingDate))
                        .Distinct()
                        .ToList()
            );
        if (incomingByBase.Count == 0)
            return;

        var legacyRows = await dbContext
            .Set<CongressionalTrade>()
            .Where(t => t.AmountFrom == 0 && t.AmountTo > 0)
            .ToListAsync(ct);
        var repairs = new List<CongressionalTrade>();
        foreach (var legacy in legacyRows)
        {
            if (!incomingByBase.TryGetValue(TradeBaseIdentity.From(legacy), out var candidates))
                continue;

            var matchingRanges = candidates
                .Where(current =>
                    legacy.FilingDate == current.FilingDate
                    && (
                        legacy.AmountTo == current.AmountFrom
                        || (current.AmountFrom == 1 && legacy.AmountTo == current.AmountTo)
                    )
                )
                .ToList();
            if (matchingRanges.Count == 1)
                repairs.Add(legacy);
        }
        if (repairs.Count == 0)
            return;

        dbContext.RemoveRange(repairs);
        await dbContext.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Removed {Count} legacy congressional trades whose zero lower bound was corrected by source replay",
            repairs.Count
        );
    }

    private sealed record TradeBaseIdentity(
        Guid CommonStockId,
        Guid CongressMemberId,
        DateOnly TransactionDate,
        CongressTransactionType TransactionType,
        string AssetName,
        string OwnerType
    )
    {
        public static TradeBaseIdentity From(CongressionalTrade trade) =>
            new(
                trade.CommonStockId,
                trade.CongressMemberId,
                trade.TransactionDate,
                trade.TransactionType,
                trade.AssetName,
                trade.OwnerType
            );
    }

    private sealed record TradeAmountRange(long AmountFrom, long AmountTo, DateOnly FilingDate);

    private sealed record InlineMetadataRepairIdentity(
        Guid CommonStockId,
        Guid CongressMemberId,
        DateOnly TransactionDate,
        CongressTransactionType TransactionType,
        string AssetName,
        string OwnerType,
        long AmountFrom,
        long AmountTo
    )
    {
        public static InlineMetadataRepairIdentity From(CongressionalTrade trade) =>
            From(trade, trade.AssetName);

        public static InlineMetadataRepairIdentity From(
            CongressionalTrade trade,
            string assetName
        ) =>
            new(
                trade.CommonStockId,
                trade.CongressMemberId,
                trade.TransactionDate,
                trade.TransactionType,
                assetName,
                trade.OwnerType,
                trade.AmountFrom,
                trade.AmountTo
            );
    }

    private sealed record LegacyInlineMetadataCandidate(
        CongressionalTrade Trade,
        InlineMetadataRepairIdentity Identity,
        string CleanedSubholding
    );
}
