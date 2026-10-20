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

    /// <summary>
    /// The CIK moves a comparison of two adjacent quarters treats as one holder: the
    /// predecessor's last 13F is the earlier quarter, the successor's first is the later one,
    /// the predecessor is still succeeded, and the group's books are comparable. The refresh
    /// stamps every pair of a group with the whole group's two values, so testing the pair tests
    /// the group (HoldingsSuccessionGroups.IsComparable).
    /// </summary>
    public static IQueryable<HolderSuccession> ComparableSuccessionsAcross(
        this DbContext dbContext,
        DateOnly later,
        DateOnly earlier
    ) =>
        dbContext
            .Set<HolderSuccession>()
            .Where(succession =>
                succession.SuccessorFirstReportDate == later
                && succession.PredecessorLastReportDate == earlier
                && succession.PredecessorGroupValue > 0
                && succession.SuccessorGroupValue > 0
                && succession.PredecessorGroupValue
                    <= HolderSuccession.MaximumComparableBookRatio * succession.SuccessorGroupValue
                && succession.SuccessorGroupValue
                    <= HolderSuccession.MaximumComparableBookRatio
                        * succession.PredecessorGroupValue
            )
            .StillSucceeded(dbContext);
}
