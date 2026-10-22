using System.Net;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Recurring reconciliation: keeps the EDGAR registrant profile (incorporation, business
/// address, periodic-report file number, business development company election) of every
/// followed issuer read, never-read issuers first and each again after <see cref="RefreshAge"/>.
/// The filing scraper stores it too, but only for the few hundred companies it enumerates a
/// day; this reads the header alone, one request per issuer.
/// </summary>
[Service]
public class IssuerRegistrantProfileRefreshService(
    ISecEdgarClient edgarClient,
    EquityIssuerRepository issuerRepository,
    SecRegistrantProfileReader profileReader,
    IOptions<WorkerOptions> workerOptions,
    ILogger<IssuerRegistrantProfileRefreshService> logger
)
{
    // Header fields rarely change, and a company that files is re-read by the filing scraper.
    internal const int RefreshAgeDays = 30;

    // The client returns null for any non-HTTP failure; a run of them is an outage.
    private const int MaxConsecutiveUnreadable = 3;

    /// <summary>
    /// Reads up to <paramref name="maxIssuers"/> due profiles, requesting the archived filing
    /// pages (several requests each) for at most <paramref name="maxArchiveReads"/> of them; an
    /// issuer left waiting for its archive stays due. Returns how many profiles were stored.
    /// </summary>
    public async Task<int> Run(
        int maxIssuers,
        int maxArchiveReads,
        CancellationToken cancellationToken
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cutoff = today.AddDays(-RefreshAgeDays);
        var tickers = workerOptions.Value.TickersToSync;
        var followed =
            tickers?.Count > 0
                ? issuerRepository.GetUsByTickers(tickers).Where(issuer => issuer.Cik != null)
                : issuerRepository.GetFollowedSecFilers();
        // Postgres sorts nulls last ascending, so never-read issuers lead by an explicit key.
        var due = await followed
            .Where(issuer =>
                issuer.SecRegistrantProfileReadOn == null
                || issuer.SecRegistrantProfileReadOn < cutoff
            )
            .OrderBy(issuer => issuer.SecRegistrantProfileReadOn != null)
            .ThenBy(issuer => issuer.SecRegistrantProfileReadOn)
            .ThenBy(issuer => issuer.Id)
            .Select(issuer => issuer.Id)
            .Take(maxIssuers)
            .ToListAsync(cancellationToken);

        var stored = 0;
        var archiveReads = 0;
        var unreadable = 0;
        foreach (var id in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One issuer tracked at a time, so each save scans only its own row.
            issuerRepository.ClearChangeTracker();
            var issuer = await issuerRepository.Get(id);
            if (issuer?.Cik == null)
                continue;

            CompanyMetadata metadata;
            try
            {
                metadata = await edgarClient.GetCompanyMetadata(issuer.Cik);
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                // Nothing to read; stamped so it does not lead the queue every cycle.
                unreadable = 0;
                await StampRead(issuer, today);
                continue;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or TaskCanceledException
                    && !cancellationToken.IsCancellationRequested
                )
            {
                logger.LogInformation(
                    exception,
                    "SEC unavailable during the issuer registrant profile refresh; resuming next cycle"
                );
                break;
            }

            if (metadata == null)
            {
                if (++unreadable >= MaxConsecutiveUnreadable)
                {
                    logger.LogInformation(
                        "EDGAR submissions unreadable for {Count} issuers in a row; resuming next cycle",
                        unreadable
                    );
                    break;
                }
                continue;
            }
            unreadable = 0;

            // A reply naming another CIK is never stored as this issuer's profile.
            if (
                CikNormalizer.Canonicalize(metadata.FilerCik)
                != CikNormalizer.Canonicalize(issuer.Cik)
            )
            {
                logger.LogWarning(
                    "EDGAR answered CIK {Requested} with CIK {Answered}; skipping its registrant profile",
                    issuer.Cik,
                    metadata.FilerCik
                );
                await StampRead(issuer, today);
                continue;
            }

            if (await profileReader.Store(issuer, metadata, archiveReads < maxArchiveReads))
                archiveReads++;
            stored++;
        }

        if (due.Count > 0)
            logger.LogInformation(
                "Issuer registrant profile refresh stored {Stored} of {Due} due profiles, {ArchiveReads} with archived filing pages",
                stored,
                due.Count,
                archiveReads
            );
        return stored;
    }

    private async Task StampRead(EquityIssuer issuer, DateOnly today)
    {
        issuer.SecRegistrantProfileReadOn = today;
        await issuerRepository.SaveChanges();
    }
}
