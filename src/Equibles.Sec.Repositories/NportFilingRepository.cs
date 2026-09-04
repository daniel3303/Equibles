using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

/// <summary>
/// Reads and writes NPORT-P portfolio reports. Unlike the other SEC filing repositories this one
/// does not derive from <c>SecFilingRepositoryBase</c>: an NPORT-P filing is not necessarily
/// attributed to a tracked stock (multi-series fund-family trusts discovered by the daily-index
/// sweep carry a <see cref="NportFiling.RegistrantCik"/> instead of a <see cref="NportFiling.CommonStockId"/>),
/// so its registrant identity is optional.
/// </summary>
public class NportFilingRepository : BaseRepository<NportFiling>
{
    public NportFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The filings attributed to a tracked stock (the fund crawled through its own feed).</summary>
    public IQueryable<NportFiling> GetByStock(CommonStock stock)
    {
        return GetAll().Where(f => f.CommonStockId == stock.Id);
    }

    public IQueryable<NportFiling> GetByAccessionNumber(string accessionNumber)
    {
        return GetAll().Where(f => f.AccessionNumber == accessionNumber);
    }

    /// <summary>The filings whose accession number is in the set — the sweep's batch dedup lookup.</summary>
    public IQueryable<NportFiling> GetByAccessionNumbers(
        IReadOnlyCollection<string> accessionNumbers
    )
    {
        return GetAll().Where(f => accessionNumbers.Contains(f.AccessionNumber));
    }

    public IQueryable<NportHolding> GetHoldings(NportFiling filing)
    {
        return DbContext.Set<NportHolding>().Where(h => h.NportFilingId == filing.Id);
    }

    /// <summary>The reported holding rows carrying the given CUSIP, across all NPORT filings.</summary>
    public IQueryable<NportHolding> GetHoldingsByCusip(string cusip)
    {
        return DbContext.Set<NportHolding>().Where(h => h.Cusip == cusip);
    }

    /// <summary>
    /// The reported holding rows carrying the stock's current CUSIP or any of its retired-CUSIP
    /// aliases (<see cref="CommonStockCusipAlias"/>), across all NPORT filings. After an issuer-level
    /// CUSIP change a fund keeps reporting the position under the old CUSIP — a laggard filer for a
    /// quarter or two, and every historical report forever — so the reverse lookup must match the
    /// alias too, mirroring the 13F import-time alias union, or the fund reads as having exited.
    ///
    /// The current CUSIP is unioned into the alias subquery (read off the stock's own row) rather
    /// than OR-ed as a separate predicate: <c>Cusip = $1 OR Cusip IN (subquery)</c> defeats the
    /// CUSIP index and degrades to a full scan of the holdings table, while a single
    /// <c>Cusip IN (subquery)</c> plans as an index semi-join. Stocks without a CUSIP have no
    /// NPORT identity, so the lookup is empty for them (callers guard, and a NULL never matches
    /// an IN) — cusip-less holding rows (bonds, foreign instruments) are never swept in.
    /// </summary>
    public IQueryable<NportHolding> GetHoldingsByStockCusip(CommonStock stock)
    {
        return GetHoldingsByListingCusip(stock, stock.Ticker);
    }

    /// <summary>
    /// Holdings carrying the exact listed security's authoritative CUSIP identity. A primary
    /// listing uses the stock's current and retired CUSIPs; a secondary listing uses only its
    /// <see cref="CommonStockListedCusip"/> rows, so sibling fund series never bleed together.
    /// </summary>
    public IQueryable<NportHolding> GetHoldingsByListingCusip(
        CommonStock stock,
        string listedTicker
    )
    {
        // The explicit not-null filters on both legs let EF's nullability analysis emit the
        // membership test as a plain equality semi-join instead of null-compensating it into
        // "= OR both-null", which would defeat the CUSIP index the same way the OR did.
        var cusips = GetCusipIdentity(stock, listedTicker);
        return DbContext
            .Set<NportHolding>()
            .Where(h => h.Cusip != null && cusips.Contains(h.Cusip));
    }

    private IQueryable<string> GetCusipIdentity(CommonStock stock, string listedTicker)
    {
        var isPrimary = string.Equals(
            listedTicker,
            stock.Ticker,
            StringComparison.OrdinalIgnoreCase
        );
        return isPrimary
            ? DbContext
                .Set<CommonStockCusipAlias>()
                .Where(a => a.CommonStockId == stock.Id)
                .Select(a => a.Cusip)
                .Union(
                    DbContext
                        .Set<CommonStock>()
                        .Where(s => s.Id == stock.Id && s.Cusip != null)
                        .Select(s => s.Cusip)
                )
            : DbContext
                .Set<CommonStockListedCusip>()
                .Where(c =>
                    c.CommonStockId == stock.Id && c.ListedTicker == listedTicker
                )
                .Select(c => c.Cusip);
    }

    public Task<bool> HasCusipIdentity(
        CommonStock stock,
        string listedTicker,
        CancellationToken cancellationToken = default
    ) => GetCusipIdentity(stock, listedTicker).AnyAsync(cancellationToken);

    /// <summary>
    /// Filings of a sweep-discovered series, identified by registrant CIK and series id (an id-less
    /// registrant collapses to its single fund). Lets the sweep tell whether it has stored this
    /// series before, so a later report holding none of our tracked stocks is still recorded as the
    /// series' latest — otherwise an earlier report would linger and an exited position would read as
    /// current.
    /// </summary>
    public IQueryable<NportFiling> GetByRegistrantCikAndSeries(
        string registrantCik,
        string seriesId
    )
    {
        return GetAll()
            .Where(f =>
                f.RegistrantCik == registrantCik
                && (
                    f.SeriesId == seriesId
                    || (string.IsNullOrEmpty(f.SeriesId) && string.IsNullOrEmpty(seriesId))
                )
            );
    }

    /// <summary>
    /// Sweep-discovered filings of the given series whose stored schedule is shorter than the row
    /// count the filing reported — that is, ones persisted while the series was still being
    /// narrowed to tracked-stock positions. The reprocess pass re-enrolls exactly these so an
    /// opted-in series heals its own history from EDGAR instead of needing a manual replay.
    ///
    /// The count comparison is the whole test and it is self-clearing: the parser stamps
    /// <see cref="NportFiling.ReportedHoldingCount"/> from the same list it returns, so a filing
    /// re-derived at full fidelity ends with the two equal and drops out. Filings whose reported
    /// count was never captured (null, pre-versioning) are left alone rather than guessed at.
    ///
    /// Series ids are compared upper-cased on both sides: EDGAR writes them uppercase, but a
    /// hand-typed configuration value must not silently match nothing.
    /// </summary>
    public IQueryable<NportFiling> GetNarrowedBelowReportedCount(
        IReadOnlyCollection<string> seriesIds
    )
    {
        var wanted = seriesIds.Select(s => s.ToUpperInvariant()).ToList();

        return GetAll()
            .Where(f => f.CommonStockId == null)
            .Where(f => f.SeriesId != null && wanted.Contains(f.SeriesId.ToUpper()))
            .Where(f => f.ReportedHoldingCount != null)
            .Where(f => f.Holdings.Count < f.ReportedHoldingCount);
    }

    /// <summary>
    /// Each fund series' most recent NPORT report whose portfolio is as of the floor date or
    /// later. Series identity never compares name text — the same fund's name varies across
    /// filings ("and"/"&amp;", "Inc"/"Inc.", stray spaces, legal renames), which would freeze a
    /// stale "latest" report under every spelling.
    ///
    /// A series is normally scoped to its registrant: a filing crawled through a tracked stock's
    /// feed is scoped by <see cref="NportFiling.CommonStockId"/>; a filing discovered by the
    /// daily-index sweep is scoped by <see cref="NportFiling.RegistrantCik"/>. A non-empty SEC
    /// <see cref="NportFiling.SeriesId"/> is globally authoritative, however, so if the same series
    /// has reached both ingestion populations only the newest filing survives. Different non-empty
    /// series ids never supersede each other. An id-less filing belongs to the registrant's single
    /// fund, so it shares identity with every filing of that registrant.
    ///
    /// The latest report is the one with the greatest report period, breaking ties by filing date
    /// and then accession number so amendments and re-filings of the same period win.
    ///
    /// Series-bearing and id-less filings are split into separate population branches concatenated
    /// with UNION ALL. A non-empty series id first competes globally through a plain equality;
    /// id-less filings then compete through their population key. Keeping both anti-joins outside
    /// OR predicates gives PostgreSQL a hashable key and avoids an O(N²) correlated scan (observed
    /// at ~8 s per call with the former combined identity predicate).
    /// </summary>
    public IQueryable<NportFiling> GetLatestPerSeries(DateOnly floor)
    {
        var filings = GetAll().Where(f => f.ReportPeriodDate >= floor);

        // A series-bearing tracked fund first competes globally by SEC series id, then against an
        // id-less filing scoped to the same tracked stock.
        var trackedSeries = filings
            .Where(f => f.CommonStockId != null && !string.IsNullOrEmpty(f.SeriesId))
            .Where(f =>
                !filings.Any(f2 =>
                    f2.SeriesId == f.SeriesId
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            )
            .Where(f =>
                !filings.Any(f2 =>
                    f2.CommonStockId == f.CommonStockId
                    && string.IsNullOrEmpty(f2.SeriesId)
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            );

        // An id-less tracked filing represents the stock's whole fund and therefore competes with
        // every filing scoped to that stock.
        var trackedIdless = filings
            .Where(f => f.CommonStockId != null && string.IsNullOrEmpty(f.SeriesId))
            .Where(f =>
                !filings.Any(f2 =>
                    f2.CommonStockId == f.CommonStockId
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            );

        // Sweep-discovered trusts: stock-less, scoped by RegistrantCik. The redundant
        // f.RegistrantCik != null inside the anti-join lambda lets EF's nullability analysis
        // emit a plain (hashable) equality instead of null-compensating it into
        // "= OR both-null" — which would give the anti-join no hash key again.
        var trustSeries = filings
            .Where(f =>
                f.CommonStockId == null
                && f.RegistrantCik != null
                && !string.IsNullOrEmpty(f.SeriesId)
            )
            .Where(f =>
                !filings.Any(f2 =>
                    f2.SeriesId == f.SeriesId
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            )
            .Where(f =>
                !filings.Any(f2 =>
                    f2.CommonStockId == null
                    && f.RegistrantCik != null
                    && f2.RegistrantCik == f.RegistrantCik
                    && string.IsNullOrEmpty(f2.SeriesId)
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            );

        // An id-less sweep filing represents the registrant's whole fund and therefore competes
        // with every sweep filing scoped to that registrant.
        var trustIdless = filings
            .Where(f =>
                f.CommonStockId == null
                && f.RegistrantCik != null
                && string.IsNullOrEmpty(f.SeriesId)
            )
            .Where(f =>
                !filings.Any(f2 =>
                    f2.CommonStockId == null
                    && f.RegistrantCik != null
                    && f2.RegistrantCik == f.RegistrantCik
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            );

        // Identity-less filings (no stock, no CIK): they all share one identity, mirroring the
        // original OR form's null-equals-null arm. Empty in practice, kept for equivalence.
        var identityless = filings
            .Where(f => f.CommonStockId == null && f.RegistrantCik == null)
            .Where(f =>
                !filings.Any(f2 =>
                    f2.CommonStockId == null
                    && f2.RegistrantCik == null
                    && (
                        f2.SeriesId == f.SeriesId
                        || string.IsNullOrEmpty(f.SeriesId)
                        || string.IsNullOrEmpty(f2.SeriesId)
                    )
                    && (
                        f2.ReportPeriodDate > f.ReportPeriodDate
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate > f.FilingDate
                        )
                        || (
                            f2.ReportPeriodDate == f.ReportPeriodDate
                            && f2.FilingDate == f.FilingDate
                            && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                        )
                    )
                )
            );

        return trackedSeries
            .Concat(trackedIdless)
            .Concat(trustSeries)
            .Concat(trustIdless)
            .Concat(identityless);
    }

    /// <summary>
    /// All filings of a single fund series, identified the same way as <see cref="GetLatestPerSeries"/>
    /// — a tracked fund by its <see cref="NportFiling.CommonStockId"/>, a sweep-discovered trust by
    /// its <see cref="NportFiling.RegistrantCik"/>, never name text; an id-less filing belongs to the
    /// registrant's single fund, so an empty <paramref name="seriesId"/> matches the id-less reports.
    /// Pass the series' own <c>CommonStockId</c> (or null for a trust) and its <c>RegistrantCik</c>.
    /// </summary>
    public IQueryable<NportFiling> GetSeriesFilings(
        Guid? commonStockId,
        string registrantCik,
        string seriesId
    )
    {
        return GetAll()
            .Where(f =>
                (
                    (commonStockId != null && f.CommonStockId == commonStockId)
                    || (
                        commonStockId == null
                        && f.CommonStockId == null
                        && f.RegistrantCik == registrantCik
                    )
                )
                && (
                    f.SeriesId == seriesId
                    || (string.IsNullOrEmpty(f.SeriesId) && string.IsNullOrEmpty(seriesId))
                )
            );
    }

    /// <summary>
    /// One report per reporting period for a single series: the latest filing of each
    /// <see cref="NportFiling.ReportPeriodDate"/> on or after <paramref name="floor"/>, so an
    /// amendment or re-file of a period collapses to the newest by filing date then accession
    /// number. This is the per-period spine of a fund's history and current portfolio — order by
    /// <c>ReportPeriodDate</c> for the time series, or take the greatest for the latest report.
    /// </summary>
    public IQueryable<NportFiling> GetSeriesReportsByPeriod(
        Guid? commonStockId,
        string registrantCik,
        string seriesId,
        DateOnly floor
    )
    {
        var filings = GetSeriesFilings(commonStockId, registrantCik, seriesId)
            .Where(f => f.ReportPeriodDate >= floor);
        return filings.Where(f =>
            !filings.Any(f2 =>
                f2.ReportPeriodDate == f.ReportPeriodDate
                && (
                    f2.FilingDate > f.FilingDate
                    || (
                        f2.FilingDate == f.FilingDate
                        && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                    )
                )
            )
        );
    }
}
