using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Extensions;

public static class Filed13FHolderQueries
{
    /// <summary>
    /// The filers that reported a 13F for a quarter: positive 13F rows or a verified zero
    /// restatement. A missing position proves an entry or exit only for a filer that is in this
    /// set for the other quarter; a first-time filer's prior quarter and a confidential-treatment
    /// quarter publish no rows and stay outside it.
    /// </summary>
    /// <remarks>
    /// Read as one index probe per filer rather than a scan of the quarter's rows: a quarter
    /// holds about 2.4M rows from under 10k filers, and the churn and mover aggregates hash
    /// this set up to four times per statement.
    /// </remarks>
    public static IQueryable<Guid> Filed13FHolderIds(this DbContext db, DateOnly reportDate) =>
        db.Set<InstitutionalHolder>()
            .Where(holder =>
                db.Set<InstitutionalHolding>()
                    .Any(h =>
                        h.InstitutionalHolderId == holder.Id
                        && h.ReportDate == reportDate
                        && h.FilingType == FilingType.Form13F
                    )
            )
            .Select(holder => holder.Id)
            .Union(
                db.Set<InstitutionalFiling>()
                    .Zero13FRestatements()
                    .Where(f => f.ReportDate == reportDate)
                    .Select(f => f.InstitutionalHolderId)
            );
}
