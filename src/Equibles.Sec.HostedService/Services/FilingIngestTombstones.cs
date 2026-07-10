using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Shared write path for filing-ingest tombstones, used by the document
/// scraper's deferred-failure path and by filing processors whose deliberate
/// deterministic skips (stale superseded amendments, pre-XML-era ownership
/// documents, malformed XML) previously persisted nothing — so every
/// enumeration re-downloaded the submission just to re-skip it. Only verdicts
/// that hold regardless of which company's feed surfaced the filing may be
/// recorded here: the tombstone is keyed by accession and consulted globally,
/// so a company-relative skip (issuer mismatch) must never be tombstoned or it
/// would suppress the real issuer's ingest. Retries never stop (backoff caps
/// at 30 days), so no filing is ever permanently lost.
/// </summary>
internal static class FilingIngestTombstones
{
    // Retry backoff: 1d, 2d, 4d, ... capped at 30d. A permanently poisonous
    // filing costs at most one download a month while a later parser fix still
    // eventually ingests it.
    internal static DateTime ComputeNextRetryAt(int attemptCount, DateTime lastAttemptAt)
    {
        const int maxBackoffDays = 30;
        var backoffDays = Math.Min(Math.Pow(2, Math.Max(attemptCount, 1) - 1), maxBackoffDays);
        return lastAttemptAt.AddDays(backoffDays);
    }

    /// <summary>
    /// Upserts the filing's tombstone: attempt count, last reason, and the next
    /// retry per the backoff. Best-effort — a failed write only means the
    /// filing is re-attempted on the next enumeration, exactly as before
    /// tombstones existed.
    /// </summary>
    internal static async Task Record(
        FailedFilingIngestRepository repository,
        string cik,
        FilingData filing,
        string reason,
        ILogger logger
    )
    {
        if (string.IsNullOrEmpty(filing.AccessionNumber))
            return;

        try
        {
            var tombstone = await repository
                .GetByAccessionNumber(filing.AccessionNumber)
                .FirstOrDefaultAsync();

            if (tombstone == null)
            {
                tombstone = new FailedFilingIngest
                {
                    AccessionNumber = filing.AccessionNumber,
                    Cik = string.IsNullOrEmpty(filing.Cik) ? cik : filing.Cik,
                    FormType = filing.Form,
                    FilingDate = filing.FilingDate,
                };
                repository.Add(tombstone);
            }

            var now = DateTime.UtcNow;
            tombstone.AttemptCount++;
            tombstone.LastAttemptAt = now;
            tombstone.NextRetryAt = ComputeNextRetryAt(tombstone.AttemptCount, now);
            tombstone.LastError = reason?.Length > 2000 ? reason[..2000] : reason;

            await repository.SaveChanges();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not tombstone failed filing {AccessionNumber}",
                filing.AccessionNumber
            );
        }
    }

    /// <summary>
    /// Removes a filing's tombstone once it finally ingests, keeping the table
    /// meaningful as "currently failing". Best-effort; usually a PK miss.
    /// </summary>
    internal static async Task Clear(
        FailedFilingIngestRepository repository,
        string accessionNumber,
        ILogger logger
    )
    {
        if (string.IsNullOrEmpty(accessionNumber))
            return;

        try
        {
            var tombstone = await repository
                .GetByAccessionNumber(accessionNumber)
                .FirstOrDefaultAsync();
            if (tombstone == null)
                return;

            repository.Delete(tombstone);
            await repository.SaveChanges();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not clear filing tombstone {AccessionNumber}",
                accessionNumber
            );
        }
    }
}
