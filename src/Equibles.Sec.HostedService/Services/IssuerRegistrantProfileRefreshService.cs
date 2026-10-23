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
/// followed issuer read, never-read issuers first and each again after <see cref="RefreshAgeDays"/>.
/// The filing scraper stores it too, but only for the few hundred companies it enumerates a
/// day; this reads the header alone, one request per issuer. Every outcome but an outage dates
/// the issuer, so the queue lives in the database: a restart resumes it and never repeats a read.
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

    // The client returns null for any non-HTTP failure, a timeout included; a run of them is an
    // outage. A lone unreadable reply is dated like a 404, or a few permanently unreadable issuers
    // would lead the queue and end every run.
    private const int MaxConsecutiveUnreadable = 3;

    /// <summary>
    /// Reads up to <paramref name="maxIssuers"/> due profiles. <see cref="RefreshRun.MoreDue"/>
    /// says a full batch was due and read without an outage, so the next batch can follow at once.
    /// </summary>
    public async Task<RefreshRun> Run(int maxIssuers, CancellationToken cancellationToken)
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
        var unreadable = 0;
        var interrupted = false;
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
                interrupted = true;
                break;
            }

            if (metadata == null)
            {
                await StampRead(issuer, today);
                if (++unreadable >= MaxConsecutiveUnreadable)
                {
                    logger.LogInformation(
                        "EDGAR submissions unreadable for {Count} issuers in a row; resuming next cycle",
                        unreadable
                    );
                    interrupted = true;
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

            await profileReader.Store(issuer, metadata);
            stored++;
        }

        if (due.Count > 0)
            logger.LogInformation(
                "Issuer registrant profile refresh stored {Stored} of {Due} due profiles",
                stored,
                due.Count
            );
        return new RefreshRun(stored, !interrupted && due.Count == maxIssuers);
    }

    private async Task StampRead(EquityIssuer issuer, DateOnly today)
    {
        issuer.SecRegistrantProfileReadOn = today;
        await issuerRepository.SaveChanges();
    }

    /// <param name="Stored">Profiles stored this run.</param>
    /// <param name="MoreDue">A full batch was due and read without an outage.</param>
    public readonly record struct RefreshRun(int Stored, bool MoreDue);
}
