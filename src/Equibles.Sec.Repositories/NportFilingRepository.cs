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
    /// later. Series are keyed by registrant + series name — <see cref="NportFiling.SeriesId"/>
    /// is absent on almost every filing. The latest report is the one with the greatest report
    /// period, breaking ties by filing date and then accession number so amendments and
    /// re-filings of the same period win.
    /// </summary>
    public IQueryable<NportFiling> GetLatestPerSeries(DateOnly floor)
    {
        var filings = GetAll().Where(f => f.ReportPeriodDate >= floor);
        return filings.Where(f =>
            !filings.Any(f2 =>
                f2.CommonStockId == f.CommonStockId
                && f2.SeriesName == f.SeriesName
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
