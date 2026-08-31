using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Data.Extensions;

public static class FormDFilingQueryOrderExtensions
{
    public static IOrderedQueryable<FormDFiling> OrderNewestFirst(
        this IQueryable<FormDFiling> query
    ) =>
        query
            .OrderByDescending(filing => filing.FilingDate)
            .ThenBy(filing => filing.AccessionNumber)
            .ThenBy(filing => filing.Id);
}
