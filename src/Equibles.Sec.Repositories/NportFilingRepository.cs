using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

public class NportFilingRepository : SecFilingRepositoryBase<NportFiling>
{
    public NportFilingRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

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
    /// Each fund series' most recent NPORT report whose portfolio is as of the floor date or
    /// later. Series identity never compares name text — the same fund's name varies across
    /// filings ("and"/"&amp;", "Inc"/"Inc.", stray spaces, legal renames), which would freeze a
    /// stale "latest" report under every spelling. Within a stock, filings carrying the same
    /// non-empty <see cref="NportFiling.SeriesId"/> are the same series; filings carrying
    /// different non-empty ids are genuinely different series and never supersede each other;
    /// and an id-less filing belongs to the registrant's single fund (listed closed-end funds
    /// file with no series id at all), so it shares identity with every filing of its stock.
    /// The latest report is the one with the greatest report period, breaking ties by filing
    /// date and then accession number so amendments and re-filings of the same period win.
    /// </summary>
    public IQueryable<NportFiling> GetLatestPerSeries(DateOnly floor)
    {
        var filings = GetAll().Where(f => f.ReportPeriodDate >= floor);
        return filings.Where(f =>
            !filings.Any(f2 =>
                f2.CommonStockId == f.CommonStockId
                && (
                    f2.SeriesId == f.SeriesId
                    || string.IsNullOrEmpty(f.SeriesId)
                    || string.IsNullOrEmpty(f2.SeriesId)
                )
                && (
                    f2.ReportPeriodDate > f.ReportPeriodDate
                    || (f2.ReportPeriodDate == f.ReportPeriodDate && f2.FilingDate > f.FilingDate)
                    || (
                        f2.ReportPeriodDate == f.ReportPeriodDate
                        && f2.FilingDate == f.FilingDate
                        && string.Compare(f2.AccessionNumber, f.AccessionNumber) > 0
                    )
                )
            )
        );
    }
}
