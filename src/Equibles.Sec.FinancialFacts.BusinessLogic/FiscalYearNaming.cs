namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>
/// One annual report as SEC Company Facts states it: the fiscal year SEC records for the filing
/// (the filer's own document fiscal year, shared by every value in that filing, comparatives
/// included) beside the fiscal year this importer stamps on the report's own full-year period.
/// </summary>
public readonly record struct AnnualReportFiscalYear(
    string Accession,
    DateOnly Filed,
    DateOnly PeriodEnd,
    int SecFiscalYear,
    int StoredFiscalYear
);

/// <summary>
/// Reads how a company names its fiscal year from its newest annual reports: the difference
/// between the year the filer wrote on the report and the year the report's period is stored
/// under (the calendar year it ends in).
/// </summary>
public static class FiscalYearNaming
{
    // Filers mistype the document fiscal year often enough that one report settles nothing
    // against another: of 132 January and February year-end companies read on 2026-11-15, the
    // newest three reports disagreed for 14. Every report read must give the same answer.
    internal const int ReportsRead = 3;

    // A year named for the calendar year it starts in ends early in the next one. Later year
    // ends are never named that way, so a lower year there is a typing error, not a naming.
    private const int LastEarlyYearEndMonth = 3;

    private const int FirstWeekDays = 7;

    /// <summary>
    /// 0 when the company names its fiscal year for the calendar year it ends in, -1 when it
    /// names it for the year it starts in, null when its newest reports do not settle it.
    /// </summary>
    public static int? Read(IEnumerable<AnnualReportFiscalYear> reports)
    {
        // One report a fiscal year: a year filed twice counts once, by its newest filing.
        var newest = reports
            .GroupBy(report => report.StoredFiscalYear)
            .Select(year =>
                year.OrderByDescending(report => report.Filed)
                    .ThenByDescending(report => report.Accession, StringComparer.Ordinal)
                    .First()
            )
            .OrderByDescending(report => report.StoredFiscalYear)
            .Take(ReportsRead)
            .ToList();
        if (newest.Count == 0)
            return null;

        var offsets = newest
            .Select(report => report.SecFiscalYear - report.StoredFiscalYear)
            .Distinct()
            .ToList();
        if (offsets.Count != 1)
            return null;

        return offsets[0] switch
        {
            0 => 0,
            -1 when newest.All(report => EndsEarlyInTheYear(report.PeriodEnd)) => -1,
            _ => null,
        };
    }

    // A 52/53-week year closing in a month's first week belongs to the month before, so a year
    // ended April 3 still ends in March and one ended January 3 ends in December.
    private static bool EndsEarlyInTheYear(DateOnly periodEnd)
    {
        var month =
            periodEnd.Day <= FirstWeekDays
                ? periodEnd.AddDays(-periodEnd.Day).Month
                : periodEnd.Month;
        return month <= LastEarlyYearEndMonth;
    }
}
