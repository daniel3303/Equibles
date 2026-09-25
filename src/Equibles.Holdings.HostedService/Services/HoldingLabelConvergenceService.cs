using System.Security.Cryptography;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Sec.FinancialFacts.Data.Registrations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Converges stored position listing labels onto the issuer's current identity after its
/// presentation listing or CUSIP claims change.
/// </summary>
/// <remarks>
/// Replays keep a stored row's label, so this pass is what stops one security living under two
/// keys; the invariants are in the holdings-13f-lane skill.
/// </remarks>
[Service]
public class HoldingLabelConvergenceService
{
    /// <summary>
    /// Issuers whose stored labels are read per statement. A multi-class issuer holds hundreds of
    /// thousands of rows, so a small batch keeps each heap read to a few seconds.
    /// </summary>
    internal const int IssuerBatchSize = 50;

    internal sealed class CandidateIssuer
    {
        public Guid Id { get; init; }
        public string PresentationTicker { get; init; }

        /// <summary>
        /// The presentation carries a CUSIP of its own. Without one, a primary row under another
        /// CUSIP may be the same security renamed onto a new listing, not a different class.
        /// </summary>
        public bool PresentationIdentified { get; init; }

        /// <summary>
        /// US tickers still trading. A retired listing holding a CUSIP is often the same
        /// security before a rename or a reverse split, so history never moves onto one.
        /// </summary>
        public List<string> LiveTickers { get; init; } = [];

        /// <summary>Retired US tickers on the issuer's other securities.</summary>
        public List<string> RetiredSiblingTickers { get; init; } = [];

        /// <summary>The fails archive's CUSIP evidence for each retired ticker of the issuer.</summary>
        public List<RetiredCusipStatement> RetiredCusipStatements { get; init; } = [];

        /// <summary>
        /// "TICKER|CUSIP" pairs a primary row may move onto: a retired sibling co-registered with the
        /// presentation on one 12(b) cover page, under a CUSIP stated for that exact ticker.
        /// </summary>
        public List<string> AdmittedRetiredCusips { get; set; } = [];
    }

    internal sealed class RetiredCusipStatement
    {
        public string Ticker { get; init; }
        public string Cusip { get; init; }
        public List<string> Candidates { get; init; } = [];
    }

    internal sealed class StoredLabel
    {
        public Guid EquityIssuerId { get; init; }
        public string Cusip { get; init; }
        public string ListedTicker { get; init; }
        public int Count { get; init; }
    }

    internal sealed record Relabel(
        Guid EquityIssuerId,
        string Cusip,
        string FromTicker,
        string ToTicker,
        int Count
    );

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingLabelConvergenceService> _logger;

    public HoldingLabelConvergenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingLabelConvergenceService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Only an issuer with a second US listing, a listing claim or a retained CUSIP-bearing
    // security can have written a sibling label, so every other issuer is never scanned.
    internal static IQueryable<CandidateIssuer> BuildCandidateQuery(
        EquiblesFinancialDbContext dbContext
    ) =>
        dbContext
            .Set<EquityIssuer>()
            .Where(issuer =>
                issuer.Presentation != null
                && (
                    dbContext
                        .Set<EquityListingCusipEvidence>()
                        .Any(evidence => evidence.EquityIssuerId == issuer.Id)
                    || issuer
                        .Securities.SelectMany(security => security.Listings)
                        .Count(listing => listing.MarketCountryCode == "US") > 1
                    || issuer.Securities.Any(security =>
                        security.Cusip != null
                        && security.Id != issuer.Presentation.Listing.EquitySecurityId
                    )
                )
            )
            .OrderBy(issuer => issuer.Id)
            .Select(issuer => new CandidateIssuer
            {
                Id = issuer.Id,
                PresentationTicker = issuer.Presentation.Listing.Ticker,
                PresentationIdentified =
                    issuer.Presentation.Listing.Security.Cusip != null
                    || dbContext
                        .Set<EquityListingCusipEvidence>()
                        .Any(evidence =>
                            evidence.EquityIssuerId == issuer.Id
                            && evidence.ListedTicker == issuer.Presentation.Listing.Ticker
                        ),
                LiveTickers = issuer
                    .Securities.SelectMany(security => security.Listings)
                    .Where(listing =>
                        listing.MarketCountryCode == "US"
                        && listing.Active
                        && listing.DelistedOn == null
                    )
                    .Select(listing => listing.Ticker)
                    .ToList(),
                RetiredSiblingTickers = issuer
                    .Securities.Where(security =>
                        security.Id != issuer.Presentation.Listing.EquitySecurityId
                    )
                    .SelectMany(security => security.Listings)
                    .Where(listing =>
                        listing.MarketCountryCode == "US"
                        && (!listing.Active || listing.DelistedOn != null)
                    )
                    .Select(listing => listing.Ticker)
                    .ToList(),
                RetiredCusipStatements = dbContext
                    .Set<EquityListingRetirementEvidence>()
                    .Where(evidence => evidence.EquityIssuerId == issuer.Id)
                    .Select(evidence => new RetiredCusipStatement
                    {
                        Ticker = evidence.ListedTicker,
                        Cusip = evidence.Cusip,
                        Candidates = evidence.HistoricalCusipBackfillCandidates,
                    })
                    .ToList(),
            });

    // Co-registration proves a distinct class, never a renamed predecessor, and the archive's own
    // statement for that ticker proves the CUSIP is the retired class's rather than one it wrongly holds.
    internal static void AdmitRetiredSiblings(
        CandidateIssuer issuer,
        IReadOnlyCollection<(string Symbol, string Accession)> registrations
    ) =>
        issuer.AdmittedRetiredCusips = issuer
            .RetiredSiblingTickers.Where(ticker =>
                !issuer.LiveTickers.Contains(ticker, StringComparer.Ordinal)
                && CoverPageRegistration.RegisteredTogether(
                    registrations,
                    issuer.PresentationTicker,
                    ticker
                )
            )
            .SelectMany(ticker =>
                issuer
                    .RetiredCusipStatements.Where(statement =>
                        string.Equals(statement.Ticker, ticker, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(statement =>
                        statement.Cusip
                        ?? (statement.Candidates.Count == 1 ? statement.Candidates[0] : null)
                    )
                    .Where(cusip => cusip != null)
                    .Select(cusip => RetiredCusipKey(ticker, cusip))
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string RetiredCusipKey(string ticker, string cusip) =>
        $"{ticker}|{cusip.Trim().ToUpperInvariant()}";

    // Only primary rows and presentation-ticker rows can move, and both filters are conditions on
    // the unique index, so an issuer holding millions of sibling-labelled rows is never heap-read.
    internal static IQueryable<StoredLabel> BuildStoredLabelQuery(
        EquiblesFinancialDbContext dbContext,
        Guid issuerId,
        string listedTicker
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Where(holding =>
                holding.EquityIssuerId == issuerId && holding.ListedTicker == listedTicker
            )
            .GroupBy(holding => new
            {
                holding.EquityIssuerId,
                holding.Cusip,
                holding.ListedTicker,
            })
            .Select(group => new StoredLabel
            {
                EquityIssuerId = group.Key.EquityIssuerId,
                Cusip = group.Key.Cusip,
                ListedTicker = group.Key.ListedTicker,
                Count = group.Count(),
            });

    /// <summary>
    /// Decides which stored labels move. Sibling moves come first, so a primary row vacates its key
    /// before a presentation-ticker row of the same position claims it.
    /// </summary>
    internal static List<Relabel> Plan(
        IEnumerable<StoredLabel> labels,
        IReadOnlyDictionary<Guid, CandidateIssuer> issuers,
        IReadOnlyDictionary<string, CusipTarget> mapping
    )
    {
        var siblingMoves = new List<Relabel>();
        var primaryMoves = new List<Relabel>();
        foreach (var label in labels)
        {
            if (!issuers.TryGetValue(label.EquityIssuerId, out var issuer))
                continue;
            var presentation = issuer.PresentationTicker;
            string resolved = null;
            var resolvesHere =
                label.Cusip != null
                && mapping.TryGetValue(label.Cusip, out var target)
                && target.CommonStockId == label.EquityIssuerId;
            if (resolvesHere)
                resolved = mapping[label.Cusip].ListedTicker;

            if (label.ListedTicker == null)
            {
                if (
                    resolvesHere
                    && resolved != null
                    && !string.Equals(resolved, presentation, StringComparison.Ordinal)
                    && issuer.PresentationIdentified
                    && (
                        issuer.LiveTickers.Contains(resolved, StringComparer.Ordinal)
                        || issuer.AdmittedRetiredCusips.Contains(
                            RetiredCusipKey(resolved, label.Cusip),
                            StringComparer.Ordinal
                        )
                    )
                )
                    siblingMoves.Add(
                        new Relabel(label.EquityIssuerId, label.Cusip, null, resolved, label.Count)
                    );
            }
            else if (
                string.Equals(label.ListedTicker, presentation, StringComparison.Ordinal)
                && (!resolvesHere || resolved == null)
            )
            {
                primaryMoves.Add(
                    new Relabel(
                        label.EquityIssuerId,
                        label.Cusip,
                        label.ListedTicker,
                        null,
                        label.Count
                    )
                );
            }
        }
        return [.. siblingMoves, .. primaryMoves];
    }

    /// <summary>
    /// The identity an issuer's labels are judged against: its presentation, its trading listings
    /// and how every CUSIP it claims resolves. Positions are rescanned only when this changes,
    /// because an import already writes the label this identity resolves to.
    /// </summary>
    internal static string Fingerprint(
        CandidateIssuer issuer,
        IReadOnlyCollection<string> claimedCusips,
        IReadOnlyDictionary<string, CusipTarget> mapping
    )
    {
        var parts = new List<string>
        {
            issuer.PresentationTicker ?? "",
            issuer.PresentationIdentified ? "identified" : "unidentified",
            string.Join(",", issuer.LiveTickers.Order(StringComparer.Ordinal)),
        };
        if (issuer.AdmittedRetiredCusips.Count > 0)
            parts.Add("retired:" + string.Join(",", issuer.AdmittedRetiredCusips));
        foreach (
            var cusip in claimedCusips
                .Select(cusip => cusip.ToUpperInvariant())
                .Distinct()
                .Order(StringComparer.Ordinal)
        )
        {
            var resolution =
                !mapping.TryGetValue(cusip, out var target) ? "-"
                : target.CommonStockId != issuer.Id ? "other"
                : target.ListedTicker ?? "";
            parts.Add($"{cusip}={resolution}");
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)))
        );
    }

    public async Task<int> Converge(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var stockRepo = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();

        var candidates = await BuildCandidateQuery(dbContext).ToListAsync(cancellationToken);
        var registrations = await CoverPageRegistration.Load(dbContext, null, cancellationToken);
        foreach (var candidate in candidates)
            AdmitRetiredSiblings(candidate, registrations.GetValueOrDefault(candidate.Id, []));
        var claims = await HoldingCusipResolution.Load(
            stockRepo,
            stockRepo.GetAll(),
            null,
            cancellationToken
        );
        var mapping = HoldingCusipResolution.Resolve(claims, []);
        var claimedCusips = claims
            .Listed.Select(claim => (claim.EquityIssuerId, claim.Cusip))
            .Concat(claims.Aliases.Select(claim => (claim.EquityIssuerId, claim.Cusip)))
            .Concat(claims.Securities.Select(claim => (claim.EquityIssuerId, claim.Cusip)))
            .ToLookup(claim => claim.EquityIssuerId, claim => claim.Cusip);
        var converged = await dbContext
            .Set<HoldingLabelConvergenceState>()
            .ToDictionaryAsync(
                state => state.EquityIssuerId,
                state => state.Fingerprint,
                cancellationToken
            );
        var due = candidates
            .Select(issuer =>
                (
                    Issuer: issuer,
                    Fingerprint: Fingerprint(issuer, [.. claimedCusips[issuer.Id]], mapping)
                )
            )
            .Where(entry =>
                !converged.TryGetValue(entry.Issuer.Id, out var fingerprint)
                || fingerprint != entry.Fingerprint
            )
            .ToList();

        var relabelled = 0;
        var occupied = 0;
        var changedQuarters = new HashSet<DateOnly>();
        foreach (var batch in due.Chunk(IssuerBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issuers = batch.ToDictionary(entry => entry.Issuer.Id, entry => entry.Issuer);
            var labels = new List<StoredLabel>();
            var unsettled = new HashSet<Guid>();
            foreach (var issuer in issuers.Values)
            {
                try
                {
                    labels.AddRange(
                        await BuildStoredLabelQuery(dbContext, issuer.Id, null)
                            .ToListAsync(cancellationToken)
                    );
                    if (issuer.PresentationTicker != null)
                        labels.AddRange(
                            await BuildStoredLabelQuery(
                                    dbContext,
                                    issuer.Id,
                                    issuer.PresentationTicker
                                )
                                .ToListAsync(cancellationToken)
                        );
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed read leaves this issuer for the next cycle, never the rest of the pass.
                    labels.RemoveAll(label => label.EquityIssuerId == issuer.Id);
                    unsettled.Add(issuer.Id);
                    _logger.LogWarning(
                        ex,
                        "Stored label read failed for issuer {IssuerId}; retrying next cycle",
                        issuer.Id
                    );
                }
            }
            var batchQuarters = new HashSet<DateOnly>();
            var fingerprints = batch.ToDictionary(
                entry => entry.Issuer.Id,
                entry => entry.Fingerprint
            );
            foreach (
                var issuerMoves in Plan(labels, issuers, mapping)
                    .GroupBy(move => move.EquityIssuerId)
            )
            {
                try
                {
                    var applied = await Apply(
                        dbContext,
                        stockRepo,
                        issuerMoves.Key,
                        fingerprints[issuerMoves.Key],
                        [.. issuerMoves],
                        cancellationToken
                    );
                    if (applied == null)
                    {
                        unsettled.Add(issuerMoves.Key);
                        continue;
                    }
                    relabelled += applied.Value.Moved;
                    occupied += issuerMoves.Sum(move => move.Count) - applied.Value.Moved;
                    batchQuarters.UnionWith(applied.Value.Dates);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One issuer's failure must not stall every issuer ordered after it.
                    dbContext.ChangeTracker.Clear();
                    unsettled.Add(issuerMoves.Key);
                    _logger.LogWarning(
                        ex,
                        "Stored label convergence failed for issuer {IssuerId}; retrying next cycle",
                        issuerMoves.Key
                    );
                }
            }

            changedQuarters.UnionWith(batchQuarters);
            await RecordConverged(
                dbContext,
                [.. batch.Where(entry => !unsettled.Contains(entry.Issuer.Id))],
                cancellationToken
            );
        }

        if (relabelled > 0 || occupied > 0)
        {
            _logger.LogWarning(
                "Converged {Relabelled} stored position label(s) of {Issuers} issuer(s) onto the "
                    + "current listing identity across {Quarters} quarter(s); {Occupied} left under "
                    + "their old label because the target position already exists",
                relabelled,
                due.Count,
                changedQuarters.Count,
                occupied
            );
        }
        return relabelled;
    }

    private static async Task RecordConverged(
        EquiblesFinancialDbContext dbContext,
        IReadOnlyCollection<(CandidateIssuer Issuer, string Fingerprint)> batch,
        CancellationToken cancellationToken
    )
    {
        var ids = batch.Select(entry => entry.Issuer.Id).ToList();
        var states = await dbContext
            .Set<HoldingLabelConvergenceState>()
            .Where(state => ids.Contains(state.EquityIssuerId))
            .ToDictionaryAsync(state => state.EquityIssuerId, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var (issuer, fingerprint) in batch)
        {
            if (!states.TryGetValue(issuer.Id, out var state))
            {
                state = new HoldingLabelConvergenceState { EquityIssuerId = issuer.Id };
                dbContext.Set<HoldingLabelConvergenceState>().Add(state);
            }
            state.Fingerprint = fingerprint;
            state.ConvergedAt = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // One issuer per transaction under the same parent lock the import flush takes, so a
    // concurrent flush cannot read a key this pass is moving. Null when the identity changed
    // after planning; the issuer is then left for the next cycle.
    private static async Task<(int Moved, List<DateOnly> Dates)?> Apply(
        EquiblesFinancialDbContext dbContext,
        EquityIssuerRepository stockRepo,
        Guid issuerId,
        string plannedFingerprint,
        List<Relabel> moves,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT "Id" FROM "EquityIssuer" WHERE "Id" = {issuerId} FOR NO KEY UPDATE
            """,
            cancellationToken
        );
        if (
            await CurrentFingerprint(dbContext, stockRepo, issuerId, cancellationToken)
            != plannedFingerprint
        )
            return null;
        var dates = new List<DateOnly>();
        foreach (var move in moves)
        {
            // The source label is written as IS NULL or '=' so the unique index serves it; a
            // null-safe comparison would heap-read every row the issuer holds.
            var update =
                move.FromTicker == null
                    ? dbContext.Database.SqlQuery<DateOnly>(
                        $"""
                        UPDATE "InstitutionalHolding" AS h SET "ListedTicker" = {move.ToTicker}
                        WHERE h."EquityIssuerId" = {issuerId}
                          AND h."ListedTicker" IS NULL
                          AND h."Cusip" IS NOT DISTINCT FROM {move.Cusip}
                          AND NOT EXISTS (
                              SELECT 1 FROM "InstitutionalHolding" AS o
                              WHERE o."EquityIssuerId" = h."EquityIssuerId"
                                AND o."InstitutionalHolderId" = h."InstitutionalHolderId"
                                AND o."ReportDate" = h."ReportDate"
                                AND o."ShareType" = h."ShareType"
                                AND o."OptionType" IS NOT DISTINCT FROM h."OptionType"
                                AND o."FilingType" = h."FilingType"
                                AND o."ListedTicker" IS NOT DISTINCT FROM {move.ToTicker})
                        RETURNING h."ReportDate" AS "Value"
                        """
                    )
                    : dbContext.Database.SqlQuery<DateOnly>(
                        $"""
                        UPDATE "InstitutionalHolding" AS h SET "ListedTicker" = {move.ToTicker}
                        WHERE h."EquityIssuerId" = {issuerId}
                          AND h."ListedTicker" = {move.FromTicker}
                          AND h."Cusip" IS NOT DISTINCT FROM {move.Cusip}
                          AND NOT EXISTS (
                              SELECT 1 FROM "InstitutionalHolding" AS o
                              WHERE o."EquityIssuerId" = h."EquityIssuerId"
                                AND o."InstitutionalHolderId" = h."InstitutionalHolderId"
                                AND o."ReportDate" = h."ReportDate"
                                AND o."ShareType" = h."ShareType"
                                AND o."OptionType" IS NOT DISTINCT FROM h."OptionType"
                                AND o."FilingType" = h."FilingType"
                                AND o."ListedTicker" IS NOT DISTINCT FROM {move.ToTicker})
                        RETURNING h."ReportDate" AS "Value"
                        """
                    );
            dates.AddRange(await update.ToListAsync(cancellationToken));
        }
        // The label and its rebuild intent must survive together: a retry cannot discover
        // the old quarter labels after the position update has committed.
        var changedDates = dates.Distinct().ToList();
        await HoldingsRollupRefresher.MarkAumSnapshotsDirty(
            dbContext,
            changedDates,
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);
        return (dates.Count, changedDates);
    }

    // Re-derives one issuer's fingerprint the way the pass does: its own claims decide which
    // CUSIPs count, and every issuer's claims on those CUSIPs decide how they resolve.
    internal static async Task<string> CurrentFingerprint(
        EquiblesFinancialDbContext dbContext,
        EquityIssuerRepository stockRepo,
        Guid issuerId,
        CancellationToken cancellationToken
    )
    {
        var issuer = await BuildCandidateQuery(dbContext)
            .Where(candidate => candidate.Id == issuerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (issuer == null)
            return null;
        var registrations = await CoverPageRegistration.Load(
            dbContext,
            [issuerId],
            cancellationToken
        );
        AdmitRetiredSiblings(issuer, registrations.GetValueOrDefault(issuerId, []));
        var own = await HoldingCusipResolution.Load(
            stockRepo,
            stockRepo.GetAll().Where(stock => stock.Id == issuerId),
            null,
            cancellationToken
        );
        var cusips = own
            .Listed.Select(claim => claim.Cusip)
            .Concat(own.Aliases.Select(claim => claim.Cusip))
            .Concat(own.Securities.Select(claim => claim.Cusip))
            .ToList();
        var claims = await HoldingCusipResolution.Load(
            stockRepo,
            stockRepo.GetAll(),
            cusips,
            cancellationToken
        );
        return Fingerprint(issuer, cusips, HoldingCusipResolution.Resolve(claims, []));
    }
}
