using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories.Extensions;

public static class HolderSuccessionQueries
{
    // A predecessor that has since filed a 13F for its successor's first quarter is no longer
    // succeeded, before the next refresh drops the pair.
    public static IQueryable<HolderSuccession> StillSucceeded(
        this IQueryable<HolderSuccession> successions,
        DbContext dbContext
    ) =>
        successions.Where(succession =>
            !dbContext
                .Set<InstitutionalFiling>()
                .Any(filing =>
                    filing.InstitutionalHolderId == succession.PredecessorHolderId
                    && filing.ReportDate == succession.SuccessorFirstReportDate
                    && filing.FilingType == FilingType.Form13F
                )
        );
}
