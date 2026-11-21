using Equibles.Sec.Data.Models;
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
    public string Note =>
        $"\n_Newer earnings release on file: 8-K filed {FiledDate:yyyy-MM-dd} (document ID "
        + $"{DocumentId}), after the latest periodic report in these facts (filed "
        + $"{LatestPeriodicFiledDate:yyyy-MM-dd}). These facts come from 10-Q and 10-K reports, so "
        + "they do not cover that release; read its statements with SearchDocument or "
        + "ReadDocumentLines using that document ID._\n";

    // A release filed the same day as the periodic report is already covered by it, so only a
    // strictly later release counts. A host without the filings module passes no repository.
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
                PeriodEnd = g.Max(f => f.PeriodEnd),
                FiscalYear = g.Max(f => f.FiscalYear),
            })
            .FirstOrDefaultAsync();
        if (latest == null)
            return null;

        var release = await documents
            .GetEarningsReleasesByIssuerId(issuerId)
            .Where(d => d.ReportingDate > latest.Filed)
            .OrderByDescending(d => d.ReportingDate)
            .Select(d => new { d.Id, d.ReportingDate })
            .FirstOrDefaultAsync();
        return release == null
            ? null
            : new NewerEarningsRelease(
                release.Id,
                release.ReportingDate,
                latest.Filed,
                latest.PeriodEnd,
                latest.FiscalYear
            );
    }
}
