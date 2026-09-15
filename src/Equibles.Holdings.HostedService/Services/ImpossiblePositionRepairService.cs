using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Withdraws the derived value from stored positions that are larger than the issuer they are in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ImpossiblePositionGuard"/> stops these at import, but a quarterly data set is
/// processed once and never revisited, so every row ingested before the guard existed keeps its
/// wrong figure forever. This pass applies the same rule to what is already stored.
/// </para>
/// <para>
/// It runs every cycle and is self-terminating: once a row is marked it no longer matches, so the
/// second pass over a repaired database does one cheap query and stops. That also makes it the
/// heal path for any row a future import writes before its issuer's size is known.
/// </para>
/// <para>
/// The scan starts from the issuer side. A few thousand issuers carry a trustworthy size; their
/// per-issuer bars are computed in memory and the holdings table is then asked, one issuer batch
/// at a time, only for the positions above that batch's lowest bar. Batches at or above
/// <see cref="CandidateSharesFloor"/> stay inside
/// <c>IX_InstitutionalHolding_ImpossiblePositionRepair</c>, a partial index over the common-share
/// rows above the floor, and the few smaller issuers form their own batch served by the
/// row-identity index; that turned three whole-table joins that timed out at ten minutes into a
/// handful of index probes.
/// </para>
/// <para>
/// The ratio guard cannot see a size stored in the wrong unit, since both figures are off by the
/// same factor. So before anything is withdrawn the exceeding positions are grouped by issuer, and
/// an issuer that <see cref="MinCorroboratingHolders"/> or more distinct filers exceed is left
/// alone and logged: the filers corroborate each other, and the stored size is what is wrong. An
/// issuer a lone filer accuses is then held against the rest of the market: when the other 13F
/// filers of the newest quarter together already hold more than the stored size, the issuer is
/// left alone too.
/// </para>
/// </remarks>
[Service]
public class ImpossiblePositionRepairService
{
    // The database narrows to positions above the multiple; the guard then makes the real decision
    // in memory, so the rule lives in exactly one place and the two paths cannot drift.
    private const int BatchSize = 500;

    /// <summary>
    /// Issuers examined per holdings query. Anchors are sorted by size first, so one batch's
    /// shared floor sits close to every member's own bar and the in-memory cut discards little.
    /// </summary>
    internal const int IssuerBatchSize = 250;

    /// <summary>
    /// The literal in the partial index's predicate. Batches whose lowest bar is at or above it
    /// are read through the index; the few issuers with a smaller bar form their own batch, asked
    /// at their true bar through the issuer indexes, so no position goes unjudged.
    /// </summary>
    internal const long CandidateSharesFloor = 1_000_000;

    /// <summary>
    /// Distinct filers that must all exceed one issuer before the issuer's own size is doubted
    /// instead of the filers. One or two impossible positions are filer errors; three managers
    /// independently reporting more than the issuer has means the stored size is what is wrong
    /// (a size stored in thousands passes the ratio guard), and withdrawing their values would
    /// accuse every holder of a legitimate position.
    /// </summary>
    internal const int MinCorroboratingHolders = 3;

    /// <summary>The 13F float one issuer's filers together report on one report date.</summary>
    internal sealed class ReportedFloat
    {
        public Guid EquityIssuerId { get; set; }
        public DateOnly ReportDate { get; set; }
        public long Shares { get; set; }
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImpossiblePositionRepairService> _logger;

    public ImpossiblePositionRepairService(
        IServiceScopeFactory scopeFactory,
        ILogger<ImpossiblePositionRepairService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>An issuer whose stored size can judge a position, with the tickers its splits are attributed to.</summary>
    internal sealed class IssuerAnchor
    {
        public Guid Id { get; set; }
        public string Ticker { get; set; }
        public List<string> SecondaryTickers { get; set; }
        public long SharesOutStanding { get; set; }
        public double MarketCapitalization { get; set; }
    }

    // The rest of the market's float for the issuers still under judgment: every 13F common-share
    // row except the exceeding ones, summed per report date, so the newest quarter can be held
    // against the stored size.
    internal static IQueryable<ReportedFloat> BuildReportedFloatQuery(
        EquiblesFinancialDbContext dbContext,
        Guid[] issuerIds,
        Guid[] excludedHoldingIds
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                issuerIds.Contains(h.EquityIssuerId)
                && h.FilingType == FilingType.Form13F
                && h.ShareType == ShareType.Shares
                && h.OptionType == null
                && !excludedHoldingIds.Contains(h.Id)
            )
            .GroupBy(h => new { h.EquityIssuerId, h.ReportDate })
            .Select(g => new ReportedFloat
            {
                EquityIssuerId = g.Key.EquityIssuerId,
                ReportDate = g.Key.ReportDate,
                Shares = g.Sum(h => h.Shares),
            });

    /// <summary>The columns the decision needs; the entity itself is loaded only for the rows being withdrawn.</summary>
    internal sealed class CandidatePosition
    {
        public Guid Id { get; set; }
        public Guid EquityIssuerId { get; set; }
        public Guid InstitutionalHolderId { get; set; }
        public long Shares { get; set; }
        public DateOnly ReportDate { get; set; }
        public string ListedTicker { get; set; }
    }

    // Exposed for the Npgsql translation pins: the anchor query must never touch the holdings
    // table, and the batch query must project columns only, because materialising the entity
    // auto-includes the owned manager legs as a second statement per batch.
    internal static IQueryable<IssuerAnchor> BuildIssuerAnchorQuery(
        EquiblesFinancialDbContext dbContext
    ) =>
        dbContext
            .Set<EquityIssuer>()
            .Where(cs =>
                cs.Presentation.Listing.Security.SharesOutstanding > 0
                && cs.Presentation.Listing.Security.MarketCapitalization > 0
            )
            .Select(cs => new IssuerAnchor
            {
                Id = cs.Id,
                Ticker = cs.Presentation.Listing.Ticker,
                SecondaryTickers = cs
                    .Securities.SelectMany(nativeSecurity => nativeSecurity.Listings)
                    .Where(nativeListing =>
                        nativeListing.MarketCountryCode == "US"
                        && (
                            nativeListing.IsDirectoryListed
                            && nativeListing.Id != cs.Presentation.EquityListingId
                        )
                    )
                    .Select(nativeListing => nativeListing.Ticker)
                    .ToList(),
                SharesOutStanding = cs.Presentation.Listing.Security.SharesOutstanding,
                MarketCapitalization = cs.Presentation.Listing.Security.MarketCapitalization,
            });

    internal static IQueryable<CandidatePosition> BuildCandidateBatchQuery(
        EquiblesFinancialDbContext dbContext,
        Guid[] issuerIds,
        long sharesFloor
    ) =>
        dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                issuerIds.Contains(h.EquityIssuerId)
                && h.ShareType == ShareType.Shares
                && !h.ValueUnavailable
                && h.Shares > sharesFloor
            )
            .Select(h => new CandidatePosition
            {
                Id = h.Id,
                EquityIssuerId = h.EquityIssuerId,
                InstitutionalHolderId = h.InstitutionalHolderId,
                Shares = h.Shares,
                ReportDate = h.ReportDate,
                ListedTicker = h.ListedTicker,
            });

    public async Task<int> Repair(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Only an issuer whose size the guard would trust can produce a withdrawal, so the others
        // are not asked about at all: Air Lease's 200 recorded shares would otherwise pull every
        // one of its legitimate positions through the batch query for the guard to reject.
        var anchors = (await BuildIssuerAnchorQuery(dbContext).ToListAsync(cancellationToken))
            .Where(anchor =>
                ImpossiblePositionGuard.AnchorIsTrustworthy(
                    anchor.SharesOutStanding,
                    anchor.MarketCapitalization
                )
            )
            .OrderBy(anchor => anchor.SharesOutStanding)
            .ToList();
        var anchorsById = anchors.ToDictionary(anchor => anchor.Id);
        var barsByIssuer = anchors.ToDictionary(
            anchor => anchor.Id,
            anchor => anchor.SharesOutStanding * ImpossiblePositionGuard.SharesOutstandingMultiple
        );

        // Candidates only, the coarse "more shares than the issuer has" filter. Whether the
        // issuer's own figures are trustworthy enough to act on is decided by the guard below.
        //
        // The filter compares an as-filed count against today's shares outstanding, so it is a
        // superset of the real matches only while restating the count cannot shrink it: that holds
        // for unsplit stocks and for reverse splits, which are exactly the cases where a legitimate
        // position would otherwise be wrongly withdrawn. A forward split moves the count the other
        // way, so a genuinely impossible position on such a stock can slip past, a miss rather
        // than a false accusation, and no worse than this pass has ever done.
        //
        // Sorted by size, so a batch's first anchor carries its lowest bar. The sub-floor issuers
        // are batched apart so that every other batch stays inside the partial index.
        var subFloor = anchors.Where(a => barsByIssuer[a.Id] < CandidateSharesFloor).ToList();
        var aboveFloor = anchors.Where(a => barsByIssuer[a.Id] >= CandidateSharesFloor).ToList();
        var candidates = new List<CandidatePosition>();
        var batches = 0;
        foreach (
            var batch in subFloor.Chunk(IssuerBatchSize).Concat(aboveFloor.Chunk(IssuerBatchSize))
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sharesFloor = barsByIssuer[batch[0].Id];
            var rows = await BuildCandidateBatchQuery(
                    dbContext,
                    batch.Select(anchor => anchor.Id).ToArray(),
                    sharesFloor
                )
                .ToListAsync(cancellationToken);
            candidates.AddRange(rows.Where(row => row.Shares > barsByIssuer[row.EquityIssuerId]));
            batches++;
        }

        _logger.LogInformation(
            "Impossible-position scan judged {Issuers} issuer(s) in {Batches} batch(es) and found "
                + "{Candidates} position(s) above the issuer multiple",
            anchors.Count,
            batches,
            candidates.Count
        );

        // Shares outstanding is today's figure, so a position has to be restated onto today's
        // basis before the two are comparable. Without this, a holder of a few percent of a company
        // that later ran a 1:50 reverse split reads as owning fifty times the issuer, and its
        // perfectly good value is withdrawn.
        var candidateStockIds = candidates.Select(c => c.EquityIssuerId).Distinct().ToList();
        var splitsByStock = (
            await StockSplitQueries
                .ForIssuers(dbContext, candidateStockIds)
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.EquityIssuerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var exceeding = new List<CandidatePosition>();
        foreach (var candidate in candidates)
        {
            var anchor = anchorsById[candidate.EquityIssuerId];
            splitsByStock.TryGetValue(candidate.EquityIssuerId, out var splits);
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    candidate.ReportDate,
                    splits,
                    candidate.ListedTicker,
                    anchor.Ticker,
                    anchor.SecondaryTickers,
                    out var shareCountFactor
                )
            )
            {
                // A split is captured but its price adjustment has not run, so there is no settled
                // basis to judge the count on. Say nothing rather than accuse the position.
                continue;
            }

            if (
                ImpossiblePositionGuard.ExceedsTheIssuer(
                    SplitAdjustment.AdjustShareCount(candidate.Shares, shareCountFactor),
                    anchor.SharesOutStanding,
                    anchor.MarketCapitalization
                )
            )
            {
                exceeding.Add(candidate);
            }
        }

        // Pooled across report dates, because today's size is one figure and every filer that
        // exceeds it is evidence against that figure. Rows withdrawn on an earlier pass are outside
        // the candidate set, so they do not corroborate a filer that arrives later; accepted.
        var doubtedIssuers = exceeding
            .GroupBy(c => c.EquityIssuerId)
            .Where(g =>
                g.Select(c => c.InstitutionalHolderId).Distinct().Count() >= MinCorroboratingHolders
            )
            .Select(g => g.Key)
            .ToHashSet();
        if (doubtedIssuers.Count > 0)
        {
            _logger.LogWarning(
                "Left {Issuers} issuer(s) unjudged because {MinHolders} or more filers each report "
                    + "more shares than the stored size, so the size is what is wrong: {Tickers}",
                doubtedIssuers.Count,
                MinCorroboratingHolders,
                string.Join(", ", doubtedIssuers.Select(id => anchorsById[id].Ticker).Order())
            );
        }

        // Second witness for the issuers a lone filer accuses: when the OTHER filers of the newest
        // quarter together already hold more than the stored size, the size is wrong and the lone
        // filer is not (PRPL stores 4.4M shares against a 62M-share reported float). Refusing a
        // genuine impossible position this way is a miss, never an accusation.
        var judged = exceeding.Where(c => !doubtedIssuers.Contains(c.EquityIssuerId)).ToList();
        var newestFloats = (
            await BuildReportedFloatQuery(
                    dbContext,
                    judged.Select(c => c.EquityIssuerId).Distinct().ToArray(),
                    judged.Select(c => c.Id).ToArray()
                )
                .ToListAsync(cancellationToken)
        )
            .GroupBy(f => f.EquityIssuerId)
            .Select(g => g.MaxBy(f => f.ReportDate)!)
            .ToList();
        var outheldIssuers = new HashSet<Guid>();
        foreach (var reportedFloat in newestFloats)
        {
            var anchor = anchorsById[reportedFloat.EquityIssuerId];
            splitsByStock.TryGetValue(reportedFloat.EquityIssuerId, out var splits);
            if (
                !HoldingValueBasis.TryResolveShareCountFactor(
                    reportedFloat.ReportDate,
                    splits,
                    null,
                    anchor.Ticker,
                    anchor.SecondaryTickers,
                    out var floatFactor
                )
                || SplitAdjustment.AdjustShareCount(reportedFloat.Shares, floatFactor)
                    > anchor.SharesOutStanding
            )
            {
                outheldIssuers.Add(reportedFloat.EquityIssuerId);
            }
        }
        if (outheldIssuers.Count > 0)
        {
            _logger.LogWarning(
                "Left {Issuers} issuer(s) unjudged because the rest of their newest-quarter filers "
                    + "together already hold more than the stored size, so the size is what is "
                    + "wrong: {Tickers}",
                outheldIssuers.Count,
                string.Join(", ", outheldIssuers.Select(id => anchorsById[id].Ticker).Order())
            );
        }

        var withdrawals = judged
            .Where(c => !outheldIssuers.Contains(c.EquityIssuerId))
            .Select(c => c.Id)
            .ToList();

        var repaired = 0;
        foreach (var chunk in withdrawals.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await dbContext
                .Set<InstitutionalHolding>()
                .Where(h => chunk.Contains(h.Id))
                .ToListAsync(cancellationToken);
            foreach (var holding in rows)
            {
                holding.Value = 0L;
                holding.ValuePending = false;
                holding.ValueUnavailable = true;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            repaired += rows.Count;
        }

        if (repaired > 0)
        {
            _logger.LogWarning(
                "Withdrew the derived value from {Repaired} position(s) reporting more shares than "
                    + "the issuer has, out of {Candidates} candidate(s); the rest sit on a share "
                    + "count the issuer's own figures cannot vouch for",
                repaired,
                candidates.Count
            );
        }

        var realigned = await RealignFilingTotals(dbContext, cancellationToken);
        if (realigned > 0)
        {
            _logger.LogWarning(
                "Re-summed {Realigned} filing rollup(s) still carrying a withdrawn position's value",
                realigned
            );
        }

        return repaired;
    }

    /// <summary>
    /// Re-sums the per-accession rollup of any filing holding a position whose value was withdrawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>InstitutionalFiling.TotalValue</c> is a stored total written once at import, so
    /// withdrawing a holding's value leaves the rollup carrying it — and the rollup is what the AUM
    /// surfaces rank on, which is exactly where the wrong figure shows. Marking the positions alone
    /// left the homepage strip still advertising a $100.8B portfolio while every position behind it
    /// read zero.
    /// </para>
    /// <para>
    /// Driven off the marked positions rather than off what this pass just changed, so it also
    /// heals filings whose positions were marked by an earlier run — and re-running it is free once
    /// the totals agree.
    /// </para>
    /// </remarks>
    private static async Task<int> RealignFilingTotals(
        EquiblesFinancialDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        var accessionNumbers = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h => h.ValueUnavailable && h.AccessionNumber != null)
            .Select(h => h.AccessionNumber)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (accessionNumbers.Count == 0)
        {
            return 0;
        }

        return await HoldingsRollupRefresher.RealignFilingTotals(
            dbContext,
            accessionNumbers,
            cancellationToken
        );
    }
}
