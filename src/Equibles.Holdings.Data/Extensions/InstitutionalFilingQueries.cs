using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Data.Extensions;

public static class InstitutionalFilingQueries
{
    // The importer persists an empty rollup only after verifying the complete source.
    public static IQueryable<InstitutionalFiling> Zero13FRestatements(
        this IQueryable<InstitutionalFiling> filings
    ) =>
        filings.Where(f =>
            f.FilingType == FilingType.Form13F
            && f.IsAmendment
            && f.PositionCount == 0
            && f.DeclaredTotalValue == 0
        );
}
