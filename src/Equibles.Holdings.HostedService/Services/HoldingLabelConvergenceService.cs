using System.Security.Cryptography;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Converges stored position listing labels onto the issuer's current identity after its
/// presentation listing or CUSIP claims change.
/// </summary>
/// <remarks>
/// <para>
/// Replays keep a stored row's label (<c>PreserveStoredObservationKeys</c> and the insert
/// trigger), so without this pass one security ends up under two keys: rows labelled with what is
/// now the presentation ticker beside new rows written as primary, and primary rows whose CUSIP
/// now names a sibling listing. Per-row comparisons key on the raw label, so the split reads as a
/// fund selling one class and buying another.
/// </para>
/// <para>
/// A label moves only when the move is certain: a presentation-ticker label becomes primary unless
/// the row's CUSIP resolves to a different listing of the issuer, and a primary row takes a sibling
/// label only when its CUSIP resolves to that still-trading sibling under the importer's own
/// precedence and the presentation carries a CUSIP of its own, so a rename stored as a new listing
/// is never mistaken for a second class. A move whose target key is already occupied is left alone, so no two
/// positions ever merge. The row keeps its id, so its manager legs stay attached.
/// </para>
/// <para>
/// Reading an issuer's positions is a heap scan (a multi-class issuer holds hundreds of thousands
/// of rows), so each issuer's identity fingerprint is stored and its positions are read again
/// only after that identity changes. The first pass reads every candidate once.
/// </para>
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
            });

    internal static IQueryable<StoredLabel> BuildStoredLabelQuery(
        EquiblesFinancialDbContext dbContext,
        Guid[] issuerIds
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Where(holding => issuerIds.Contains(holding.EquityIssuerId))
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
                    && issuer.PresentationIdentified
                    && issuer.LiveTickers.Contains(resolved, StringComparer.Ordinal)
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
            var labels = await BuildStoredLabelQuery(dbContext, [.. issuers.Keys])
                .ToListAsync(cancellationToken);
            var batchQuarters = new HashSet<DateOnly>();
            foreach (
                var issuerMoves in Plan(labels, issuers, mapping)
                    .GroupBy(move => move.EquityIssuerId)
            )
            {
                var (moved, dates) = await Apply(
                    dbContext,
                    issuerMoves.Key,
                    [.. issuerMoves],
                    cancellationToken
                );
                relabelled += moved;
                occupied += issuerMoves.Sum(move => move.Count) - moved;
                batchQuarters.UnionWith(dates);
            }

            // Quarter aggregates group by listing, so they are marked before the batch is recorded
            // as converged; a crash in between rescans the batch rather than losing the rebuild.
            if (batchQuarters.Count > 0)
                await HoldingsRollupRefresher.MarkAumSnapshotsDirty(
                    dbContext,
                    batchQuarters,
                    cancellationToken
                );
            changedQuarters.UnionWith(batchQuarters);
            await RecordConverged(dbContext, batch, cancellationToken);
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
    // concurrent flush cannot read a key this pass is moving.
    private static async Task<(int Moved, List<DateOnly> Dates)> Apply(
        EquiblesFinancialDbContext dbContext,
        Guid issuerId,
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
        var dates = new List<DateOnly>();
        foreach (var move in moves)
        {
            dates.AddRange(
                await dbContext
                    .Database.SqlQuery<DateOnly>(
                        $"""
                        UPDATE "InstitutionalHolding" AS h SET "ListedTicker" = {move.ToTicker}
                        WHERE h."EquityIssuerId" = {issuerId}
                          AND h."Cusip" IS NOT DISTINCT FROM {move.Cusip}
                          AND h."ListedTicker" IS NOT DISTINCT FROM {move.FromTicker}
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
                    .ToListAsync(cancellationToken)
            );
        }
        await transaction.CommitAsync(cancellationToken);
        return (dates.Count, dates.Distinct().ToList());
    }
}
