using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// The earnings release an issuer filed after the newest periodic report its structured facts
/// come from. Structured facts wait for the 10-Q or 10-K, so the release holds the only stored
/// statements for its period; a filer that stopped reporting never files one, so the note
/// promises nothing about when facts follow.
/// </summary>
public sealed record NewerEarningsRelease(
    Guid DocumentId,
    DateOnly FiledDate,
    DateOnly LatestPeriodicFiledDate,
    DateOnly LatestPeriodEnd,
    int LatestFiscalYear
)
{
    // Dates cannot prove which period a release reports (a release can follow a late 10-K by a
    // day), so the note states the dates and leaves the period to the reader of the release.
    public string Note =>
        $"\n_An earnings release was filed {FiledDate:yyyy-MM-dd} (8-K, document ID {DocumentId}), "
        + $"after the latest periodic report in these facts (filed {LatestPeriodicFiledDate:yyyy-MM-dd}, "
        + $"period ended {LatestPeriodEnd:yyyy-MM-dd}). Where it reports a later period, these facts do "
        + "not hold its figures: read them from the release with SearchDocument or ReadDocumentLines "
        + "using that document ID._\n";

    // A release filed the same day as the periodic report is already covered by it, and one filed
    // before another quarter could have ended restates the reported quarter (many filers publish
    // the release a day after the 10-Q). A host without the filings module passes no repository.
    public static async Task<NewerEarningsRelease> Find(
        FinancialFactRepository facts,
        DocumentRepository documents,
        Guid issuerId
    )
    {
        if (documents == null)
            return null;

        // The periodic forms an 8-K filer reports on, spelled out for EF.
        var latest = await facts
            .GetConsolidatedByIssuerId(issuerId)
            .Where(f =>
                (
                    f.Form == DocumentType.TenQ
                    || f.Form == DocumentType.TenQa
                    || f.Form == DocumentType.TenK
                    || f.Form == DocumentType.TenKa
                )
                && f.PeriodEnd <= f.FiledDate
            )
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Filed = g.Max(f => f.FiledDate),
                // Flows end on the reported period; a cover-page share count is dated weeks later.
                FlowEnd = g.Max(f => f.PeriodEnd > f.PeriodStart ? (DateOnly?)f.PeriodEnd : null),
                FiscalYear = g.Max(f => f.FiscalYear),
            })
            .FirstOrDefaultAsync();
        if (latest?.FlowEnd is not { } flowEnd)
            return null;

        var nextQuarterEnd = flowEnd.AddDays(FiscalPeriodSpanDays.MinQuarter);
        var after = latest.Filed > nextQuarterEnd ? latest.Filed : nextQuarterEnd;
        var release = await documents
            .GetEarningsReleasesByIssuerId(issuerId)
            .Where(d => d.ReportingDate > after)
            .OrderByDescending(d => d.ReportingDate)
            .Select(d => new { d.Id, d.ReportingDate })
            .FirstOrDefaultAsync();
        return release == null
            ? null
            : new NewerEarningsRelease(
                release.Id,
                release.ReportingDate,
                latest.Filed,
                flowEnd,
                latest.FiscalYear
            );
    }
}
