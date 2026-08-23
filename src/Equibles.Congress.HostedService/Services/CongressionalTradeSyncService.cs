using Equibles.CommonStocks.Data.Helpers;
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
    private readonly CongressionalTradeIssuerResolver _issuerResolver;

    public CongressionalTradeSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> workerOptions,
        ILogger<CongressionalTradeSyncService> logger,
        ErrorReporter errorReporter,
        CongressionalFilingLedger filingLedger,
        CongressionalTradeImportLedger importLedger,
        CongressionalTradeIssuerResolver issuerResolver
    )
    {
        _scopeFactory = scopeFactory;
        _workerOptions = workerOptions.Value;
        _logger = logger;
        _errorReporter = errorReporter;
        _filingLedger = filingLedger;
        _importLedger = importLedger;
        _issuerResolver = issuerResolver;
    }

    // Congressional trade disclosures are available from 2012 (STOCK Act).
    private static readonly DateOnly EarliestAvailableDate = new(2012, 4, 1);
    private const int LegacyTradeParserVersion = 4;
    private const int CurrentTradeParserVersion = 5;
    private const int ReprocessPerCycleLimit = 1_000;

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

        var parserVersion = await GetActiveTradeParserVersion(ct);
        var batches = await FetchBatches(fromDate, toDate, parserVersion, ct);
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

        var linked = await _issuerResolver.RelinkUnresolved(ct);
        if (linked > 0)
        {
            _logger.LogInformation(
                "Linked {Count} previously unresolved congressional trades from new ticker evidence",
                linked
            );
        }

        // Only after the transactions are committed: a failed persist above
        // throws before this point, so unrecorded filings re-fetch next cycle
        // instead of being lost.
        foreach (var batch in batches)
            await RecordBatch(batch, outcome, parserVersion, ct);
    }

    private async Task<int> GetActiveTradeParserVersion(CancellationToken cancellationToken)
    {
        var evidenceBackfillPending = await _filingLedger.HasPendingTickerEvidence(
            cancellationToken
        );

        var tickerScopeRestricted = (_workerOptions.TickersToSync ?? [])
            .Select(TickerNormalizer.NormalizeIdentity)
            .Any(ticker => ticker != null);
        var version = SelectTradeParserVersion(evidenceBackfillPending, tickerScopeRestricted);
        _logger.LogInformation(
            "Congressional trade parser version {ParserVersion} active; ticker evidence backfill pending: {EvidenceBackfillPending}; ticker scope restricted: {TickerScopeRestricted}",
            version,
            evidenceBackfillPending,
            tickerScopeRestricted
        );
        return version;
    }

    internal static int SelectTradeParserVersion(
        bool evidenceBackfillPending,
        bool tickerScopeRestricted
    ) =>
        evidenceBackfillPending || tickerScopeRestricted
            ? LegacyTradeParserVersion
            : CurrentTradeParserVersion;

    private async Task<List<TradeFetchBatch>> FetchBatches(
        DateOnly fromDate,
        DateOnly toDate,
        int parserVersion,
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
                parserVersion,
                ReprocessPerCycleLimit
            );
            var current = await FetchRange(kind, fromDate, toDate, processed, ct);
            batches.Add(new TradeFetchBatch(kind, current, null));

            var archiveYear = await _importLedger.GetNextYear(
                kind,
                parserVersion,
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
                parserVersion,
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
        int parserVersion,
        CancellationToken ct
    )
    {
        var recordable = FilterRecordable(batch.Result.ProcessedFilings, outcome);
        await _filingLedger.RecordProcessed(batch.Kind, recordable, ct, parserVersion);

        if (
            batch.ArchiveYear == null
            || !batch.Result.IsComplete
            || recordable.Count != batch.Result.ProcessedFilings.Count
        )
            return;

        await _importLedger.RecordCompleted(
            batch.Kind,
            batch.ArchiveYear.Value,
            parserVersion,
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

    // A filing is only retired once everything it disclosed is accounted for. A ticker with no
    // safe issuer match is accounted for as an unlinked source row; malformed rows and ambiguous
    // legacy adoption remain retryable.
    internal static List<ProcessedFiling> FilterRecordable(
        List<ProcessedFiling> filings,
        TradePersistOutcome outcome
    ) => filings.Where(f => !outcome.UnpersistedSourceIds.Contains(f.SourceId)).ToList();

    /// <summary>
    /// The persistence outcome of one sync cycle: filings named here had
    /// transactions that were parsed but not stored, so they must not (yet)
    /// be recorded as ingested.
    /// </summary>
    internal sealed record TradePersistOutcome(IReadOnlySet<string> UnpersistedSourceIds)
    {
        public static readonly TradePersistOutcome Empty = new(new HashSet<string>());
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

        var configuredTickers = (_workerOptions.TickersToSync ?? [])
            .Select(TickerNormalizer.NormalizeIdentity)
            .Where(ticker => ticker != null)
            .ToHashSet(StringComparer.Ordinal);
        var tickered = transactions
            .Where(transaction => TickerNormalizer.NormalizeIdentity(transaction.Ticker) != null)
            .Where(transaction =>
                configuredTickers.Count == 0
                || configuredTickers.Contains(
                    TickerNormalizer.NormalizeIdentity(transaction.Ticker)
                )
            )
            .ToList();

        // A source row dated after its own filing is never recordable, even when the ticker is
        // absent or not tracked. Keep the whole filing retryable rather than checkpointing a
        // semantically malformed disclosure.
        var unpersistedSourceIds = transactions
            .Where(t => t.SourceId != null && t.TransactionDate > t.FilingDate)
            .Select(t => t.SourceId)
            .ToHashSet();

        if (tickered.Count == 0)
            return new TradePersistOutcome(unpersistedSourceIds);

        var resolutions = await _issuerResolver.Resolve(tickered, ct);
        _logger.LogInformation(
            "Resolved {Resolved}/{Tickered} tickered transactions to an issuer",
            resolutions.Count(pair => pair.Value.HasValue),
            tickered.Count
        );

        var members = await UpsertCongressMembers(tickered, dbContext, memberRepository, ct);

        // Mirrors BuildTrades' member-not-found guard: those rows are parsed
        // but never stored, so their filings must not be recorded as ingested.
        unpersistedSourceIds.UnionWith(
            tickered
                .Where(t =>
                    t.SourceId != null
                    && !members.ContainsKey(
                        DisclosureParsingHelper.NormalizeMemberName(t.MemberName)
                    )
                )
                .Select(t => t.SourceId)
        );

        var trades = BuildTrades(tickered, members, resolutions);
        unpersistedSourceIds.UnionWith(await PersistTrades(trades, dbContext, ct));

        return new TradePersistOutcome(unpersistedSourceIds);
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
        List<DisclosureTransaction> tickered,
        Dictionary<string, CongressMember> members,
        IReadOnlyDictionary<DisclosureTransaction, Guid?> resolutions
    )
    {
        var trades = new List<CongressionalTrade>();

        foreach (var tx in tickered)
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

            trades.Add(
                new CongressionalTrade
                {
                    CongressMemberId = member.Id,
                    CommonStockId = resolutions.GetValueOrDefault(tx),
                    FiledTicker = TickerNormalizer.NormalizeIdentity(tx.Ticker) ?? "",
                    FilingKind = tx.FilingKind,
                    SourceId = tx.SourceId,
                    SourceRowIndex = tx.SourceRowIndex,
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

    private async Task<IReadOnlySet<string>> PersistTrades(
        List<CongressionalTrade> trades,
        EquiblesFinancialDbContext dbContext,
        CancellationToken ct
    )
    {
        if (trades.Count == 0)
            return new HashSet<string>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        await RepairLegacyInlineSubholdingNames(trades, dbContext, ct);
        await RepairLegacyEmptyFiledMetadata(trades, dbContext, ct);
        await RepairLegacyZeroFloorAmounts(trades, dbContext, ct);
        RemoveUnidentifiedMetadataDuplicates(trades);
        var unpersistedSourceIds = await AdoptLegacyRows(trades, dbContext, ct);

        if (trades.Count > 0)
        {
            await dbContext
                .Set<CongressionalTrade>()
                .UpsertRange(trades)
                .On(t => new
                {
                    t.FilingKind,
                    t.SourceId,
                    t.SourceRowIndex,
                })
                .WhenMatched(
                    (existing, incoming) =>
                        new CongressionalTrade
                        {
                            CommonStockId = incoming.CommonStockId,
                            CongressMemberId = incoming.CongressMemberId,
                            TransactionDate = incoming.TransactionDate,
                            FilingDate = incoming.FilingDate,
                            TransactionType = incoming.TransactionType,
                            OwnerType = incoming.OwnerType,
                            AssetName = incoming.AssetName,
                            AssetType = incoming.AssetType,
                            Subholding = incoming.Subholding,
                            AmountFrom = incoming.AmountFrom,
                            AmountTo = incoming.AmountTo,
                        }
                )
                .RunAsync(ct);
        }

        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Upserted {Count} congressional trades (duplicates skipped)",
            trades.Count
        );

        return unpersistedSourceIds;
    }

    /// <summary>
    /// Gives pre-source-identity rows their stable filing key on replay. Company identity is
    /// deliberately excluded from the match because correcting a reused ticker is the purpose of
    /// the replay. Ambiguous matches are refused and keep the filing retryable.
    /// </summary>
    private async Task<HashSet<string>> AdoptLegacyRows(
        List<CongressionalTrade> incoming,
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var unpersistedSourceIds = new HashSet<string>();
        var duplicateSourceKeys = incoming
            .Where(trade => trade.SourceId != null)
            .GroupBy(trade => new
            {
                trade.FilingKind,
                trade.SourceId,
                trade.SourceRowIndex,
            })
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var duplicate in duplicateSourceKeys)
        {
            var refusedRows = duplicate.ToList();
            incoming.RemoveAll(refusedRows.Contains);
            unpersistedSourceIds.UnionWith(refusedRows.Select(trade => trade.SourceId));
            _logger.LogWarning(
                "Deferred congressional filing {SourceId} because source row {SourceRowIndex} appeared more than once",
                duplicate.Key.SourceId,
                duplicate.Key.SourceRowIndex
            );
        }

        var sourceRows = incoming.Where(trade => trade.SourceId != null).ToList();
        if (sourceRows.Count == 0)
            return unpersistedSourceIds;

        var sourceIds = sourceRows.Select(trade => trade.SourceId).Distinct().ToList();
        var storedSourceRows = await dbContext
            .Set<CongressionalTrade>()
            .AsNoTracking()
            .Where(trade => trade.SourceId != null && sourceIds.Contains(trade.SourceId))
            .ToListAsync(cancellationToken);
        var storedBySourceIdentity = storedSourceRows.ToDictionary(TradeSourceIdentity.From);
        var tickerConflicts = sourceRows
            .Where(incomingTrade =>
                storedBySourceIdentity.TryGetValue(
                    TradeSourceIdentity.From(incomingTrade),
                    out var storedTrade
                )
                && storedTrade.FiledTicker != incomingTrade.FiledTicker
            )
            .ToList();
        foreach (var conflict in tickerConflicts)
        {
            incoming.Remove(conflict);
            unpersistedSourceIds.Add(conflict.SourceId);
            _logger.LogWarning(
                "Deferred congressional filing {SourceId} because source row {SourceRowIndex} changed its filed ticker",
                conflict.SourceId,
                conflict.SourceRowIndex
            );
        }

        sourceRows = incoming.Where(trade => trade.SourceId != null).ToList();
        if (sourceRows.Count == 0)
            return unpersistedSourceIds;

        var memberIds = sourceRows.Select(trade => trade.CongressMemberId).Distinct().ToList();
        var firstDate = sourceRows.Min(trade => trade.TransactionDate);
        var lastDate = sourceRows.Max(trade => trade.TransactionDate);
        var legacyRows = await dbContext
            .Set<CongressionalTrade>()
            .Where(trade =>
                trade.SourceId == null
                && memberIds.Contains(trade.CongressMemberId)
                && trade.TransactionDate >= firstDate
                && trade.TransactionDate <= lastDate
            )
            .ToListAsync(cancellationToken);
        var legacyByIdentity = legacyRows
            .GroupBy(LegacySourceIdentity.From)
            .ToDictionary(group => group.Key, group => group.ToList());
        var incomingByIdentity = sourceRows
            .GroupBy(LegacySourceIdentity.From)
            .ToDictionary(group => group.Key, group => group.ToList());
        var adopted = new List<CongressionalTrade>();
        var refused = new List<CongressionalTrade>();

        foreach (var (identity, candidates) in incomingByIdentity)
        {
            if (!legacyByIdentity.TryGetValue(identity, out var legacyCandidates))
                continue;

            if (legacyCandidates.Count != 1)
            {
                refused.AddRange(candidates);
                unpersistedSourceIds.UnionWith(candidates.Select(trade => trade.SourceId));
                _logger.LogWarning(
                    "Deferred congressional trade source adoption because {IncomingCount} source rows matched {LegacyCount} legacy rows",
                    candidates.Count,
                    legacyCandidates.Count
                );
                continue;
            }

            // The old semantic unique index could collapse two identical filed source rows into
            // one legacy row. Adopt it as the lowest source ordinal; remaining rows insert under
            // their own stable keys.
            var source = candidates
                .OrderBy(candidate => candidate.SourceRowIndex)
                .ThenBy(candidate => candidate.SourceId, StringComparer.Ordinal)
                .First();
            var legacy = legacyCandidates[0];
            legacy.CommonStockId = source.CommonStockId;
            legacy.FiledTicker = source.FiledTicker;
            legacy.FilingKind = source.FilingKind;
            legacy.SourceId = source.SourceId;
            legacy.SourceRowIndex = source.SourceRowIndex;
            legacy.FilingDate = source.FilingDate;
            legacy.AssetType = source.AssetType;
            legacy.Subholding = source.Subholding;
            adopted.Add(source);
        }

        incoming.RemoveAll(trade => adopted.Contains(trade) || refused.Contains(trade));
        if (adopted.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Adopted {Count} legacy congressional trades using stable source-row identity",
                adopted.Count
            );
        }

        return unpersistedSourceIds;
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
        Guid? CommonStockId,
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
        Guid? CommonStockId,
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

    private sealed record LegacySourceIdentity(
        Guid CongressMemberId,
        DateOnly TransactionDate,
        CongressTransactionType TransactionType,
        string AssetName,
        string OwnerType,
        long AmountFrom,
        long AmountTo,
        string AssetType,
        string Subholding
    )
    {
        public static LegacySourceIdentity From(CongressionalTrade trade) =>
            new(
                trade.CongressMemberId,
                trade.TransactionDate,
                trade.TransactionType,
                trade.AssetName,
                trade.OwnerType,
                trade.AmountFrom,
                trade.AmountTo,
                trade.AssetType,
                trade.Subholding
            );
    }

    private sealed record TradeSourceIdentity(
        CongressionalFilingKind? FilingKind,
        string SourceId,
        int? SourceRowIndex
    )
    {
        public static TradeSourceIdentity From(CongressionalTrade trade) =>
            new(trade.FilingKind, trade.SourceId, trade.SourceRowIndex);
    }
}
