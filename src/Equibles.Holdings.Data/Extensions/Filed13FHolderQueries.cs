using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Extensions;

public static class Filed13FHolderQueries
{
    /// <summary>
    /// The filers that reported a 13F for a quarter: positive 13F rows or a verified zero
    /// restatement. A missing position proves an exit only for a filer in this set; a
    /// confidential-treatment quarter publishes no rows and stays outside it.
    /// </summary>
    public static IQueryable<Guid> Filed13FHolderIds(this DbContext db, DateOnly reportDate) =>
        db.Set<InstitutionalHolding>()
            .Where(h => h.ReportDate == reportDate && h.FilingType == FilingType.Form13F)
            .Select(h => h.InstitutionalHolderId)
            .Union(
                db.Set<InstitutionalFiling>()
                    .Zero13FRestatements()
                    .Where(f => f.ReportDate == reportDate)
                    .Select(f => f.InstitutionalHolderId)
            );
}
