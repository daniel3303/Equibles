using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Repositories;

public class ReleaseStatementFigureRepository : BaseRepository<ReleaseStatementFigure>
{
    public ReleaseStatementFigureRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<ReleaseStatementFigure> GetByIssuerId(Guid issuerId) =>
        GetAll().Where(figure => figure.EquityIssuerId == issuerId);

    /// <summary>
    /// The release figures a company's periodic reports do not cover yet, one per concept and
    /// period (the latest release wins), each with its concept. A figure stops being served
    /// once a 10-Q or 10-K on file reports a period ending on or after the figure's: the
    /// periodic report is then the source, whatever the release said. The test is on the period
    /// end alone because a release's period start is derived and can sit a day off the filing's.
    /// </summary>
    public async Task<List<ReleaseStatementFigure>> GetUncovered(
        Guid issuerId,
        FinancialFactRepository facts,
        CancellationToken cancellationToken = default
    )
    {
        var figures = await GetByIssuerId(issuerId)
            .Include(figure => figure.FinancialConcept)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (figures.Count == 0)
            return figures;

        var earliest = figures.Min(figure => figure.PeriodEnd);
        var covered = await facts
            .GetByIssuerId(issuerId)
            .Where(f =>
                f.DimensionsKey == ""
                && f.PeriodEnd >= earliest
                && f.PeriodEnd > f.PeriodStart
                && (
                    f.Form == DocumentType.TenQ
                    || f.Form == DocumentType.TenQa
                    || f.Form == DocumentType.TenK
                    || f.Form == DocumentType.TenKa
                )
            )
            .MaxAsync(f => (DateOnly?)f.PeriodEnd, cancellationToken);

        return figures
            .Where(figure => covered == null || figure.PeriodEnd > covered)
            .GroupBy(figure => (figure.FinancialConceptId, figure.PeriodStart, figure.PeriodEnd))
            .Select(group => group.OrderByDescending(figure => figure.FiledDate).First())
            .ToList();
    }
}
