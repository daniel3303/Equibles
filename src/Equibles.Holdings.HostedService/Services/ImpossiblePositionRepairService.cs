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
/// at a time, only for the positions above that batch's bar. Every batch query stays inside
/// <c>IX_InstitutionalHolding_ImpossiblePositionRepair</c>, a partial index over the common-share
/// rows above <see cref="CandidateSharesFloor"/>, which is what turned three whole-table joins
/// that timed out at ten minutes into a handful of index probes.
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
    /// The smallest share count the scan reads. It is the literal in the partial index's
    /// predicate, so a batch whose bar is lower is raised to it and every batch query stays
    /// index-served. Consequence: an issuer with fewer than half this many shares outstanding is
    /// judged only on positions above the floor, a miss on the smallest floats and never a false
    /// accusation.
    /// </summary>
    internal const long CandidateSharesFloor = 1_000_000;

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

    /// <summary>The columns the decision needs; the entity itself is loaded only for the rows being withdrawn.</summary>
    internal sealed class CandidatePosition
    {
        public Guid Id { get; set; }
        public Guid EquityIssuerId { get; set; }
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

        // Candidates only — the coarse "more shares than the issuer has" filter. Whether the
        // issuer's own figures are trustworthy enough to act on is decided by the guard below.
        //
        // The filter compares an as-filed count against today's shares outstanding, so it is a
        // superset of the real matches only while restating the count cannot shrink it: that holds
        // for unsplit stocks and for reverse splits, which are exactly the cases where a legitimate
        // position would otherwise be wrongly withdrawn. A forward split moves the count the other
        // way, so a genuinely impossible position on such a stock can slip past — a miss rather
        // than a false accusation, and no worse than this pass has ever done.
        var candidates = new List<CandidatePosition>();
        var batches = 0;
        foreach (var batch in anchors.Chunk(IssuerBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Sorted by size, so the first anchor's bar is the batch's lowest; the floor keeps the
            // query inside the partial index and the exact per-issuer bar is applied in memory.
            var sharesFloor = Math.Max(barsByIssuer[batch[0].Id], CandidateSharesFloor);
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
            await dbContext
                .Set<StockSplit>()
                .Where(s => candidateStockIds.Contains(s.EquityIssuerId))
                .ToListAsync(cancellationToken)
        )
            .GroupBy(s => s.EquityIssuerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var withdrawals = new List<Guid>();
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
                withdrawals.Add(candidate.Id);
            }
        }

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
