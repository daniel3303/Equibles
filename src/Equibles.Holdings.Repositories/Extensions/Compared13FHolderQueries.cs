using Equibles.Holdings.Data.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories.Extensions;

public static class Compared13FHolderQueries
{
    /// <summary>
    /// The filers whose positions at <paramref name="reportDate"/> the buy and sell movers
    /// compare against <paramref name="comparedDate"/>: those that reported a 13F for the
    /// compared quarter, and either side of a comparable CIK move between the two quarters,
    /// whose other side stands in for that filing. Everyone else's positions are kept apart as
    /// unmatched, so a first-time filer's book is no buy and a confidential quarter no sale,
    /// while a CIK move compares the successor's book with the predecessor's.
    /// </summary>
    public static IQueryable<Guid> Compared13FHolderIds(
        this DbContext dbContext,
        DateOnly reportDate,
        DateOnly comparedDate
    )
    {
        var filed = dbContext.Filed13FHolderIds(comparedDate);
        return reportDate > comparedDate
            ? filed.Union(
                dbContext
                    .ComparableSuccessionsAcross(reportDate, comparedDate)
                    .Select(succession => succession.SuccessorHolderId)
            )
            : filed.Union(
                dbContext
                    .ComparableSuccessionsAcross(comparedDate, reportDate)
                    .Select(succession => succession.PredecessorHolderId)
            );
    }
}
