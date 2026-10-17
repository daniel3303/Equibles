using System.Net;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.Core.AutoWiring;
using Equibles.Core.Extensions;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Recurring reconciliation: keeps an EDGAR profile (entity type, SIC, filer category) for every
/// 13F filer, fetching new filers first and re-reading each profile after <see cref="RefreshAge"/>.
/// Most 13F managers are not tracked issuers, so this is where their SEC classification lives.
/// </summary>
[Service]
public class HolderFilerProfileRefreshService(
    ISecEdgarClient edgarClient,
    InstitutionalHolderRepository holderRepository,
    SecFilerProfileRepository profileRepository,
    ILogger<HolderFilerProfileRefreshService> logger
)
{
    // SEC rarely reassigns a filer's SIC or category, so a half-year refresh keeps them current.
    internal static readonly TimeSpan RefreshAge = TimeSpan.FromDays(180);

    // The client returns null for any non-HTTP failure, a timeout included; a run of them is an outage.
    private const int MaxConsecutiveUnreadable = 3;

    /// <summary>Fetches up to <paramref name="maxFilers"/> due profiles and returns how many were stored.</summary>
    public async Task<int> Run(int maxFilers, CancellationToken cancellationToken)
    {
        var ciks = (await holderRepository.Get13FFilerCiks().ToListAsync(cancellationToken))
            .Select(CikNormalizer.Canonicalize)
            .Where(cik => cik != null)
            .Distinct()
            .ToList();
        var fetchedAt = await profileRepository
            .GetAll()
            .Select(profile => new { profile.Cik, profile.FetchedAt })
            .ToDictionaryAsync(
                profile => profile.Cik,
                profile => profile.FetchedAt,
                cancellationToken
            );
        var cutoff = DateTime.UtcNow - RefreshAge;
        var due = ciks.Where(cik => !fetchedAt.TryGetValue(cik, out var at) || at < cutoff)
            .OrderBy(cik => fetchedAt.TryGetValue(cik, out var at) ? at : DateTime.MinValue)
            .ThenBy(cik => cik, StringComparer.Ordinal)
            .Take(maxFilers)
            .ToList();

        var stored = 0;
        var unreadable = 0;
        foreach (var cik in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompanyMetadata metadata;
            try
            {
                metadata = await edgarClient.GetCompanyMetadata(cik);
            }
            catch (HttpRequestException exception)
                when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                unreadable = 0;
                await Store(cik, null);
                stored++;
                continue;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or TaskCanceledException
                    && !cancellationToken.IsCancellationRequested
                )
            {
                logger.LogInformation(
                    exception,
                    "SEC unavailable during the 13F filer profile refresh; resuming next cycle"
                );
                break;
            }

            // A reply naming another CIK is never filed under the one asked for.
            if (metadata != null && CikNormalizer.Canonicalize(metadata.FilerCik) != cik)
            {
                logger.LogWarning(
                    "EDGAR answered CIK {Requested} with CIK {Answered}; skipping its profile",
                    cik,
                    metadata.FilerCik
                );
                continue;
            }

            if (metadata == null)
            {
                if (++unreadable >= MaxConsecutiveUnreadable)
                {
                    logger.LogInformation(
                        "EDGAR profiles unreadable for {Count} filers in a row; resuming next cycle",
                        unreadable
                    );
                    break;
                }
                continue;
            }

            unreadable = 0;
            await Store(cik, metadata);
            stored++;
        }

        if (due.Count > 0)
            logger.LogInformation(
                "13F filer profile refresh stored {Stored} of {Due} due profiles",
                stored,
                due.Count
            );
        return stored;
    }

    private async Task Store(string cik, CompanyMetadata metadata)
    {
        var profile = await profileRepository.GetByCiks([cik]).SingleOrDefaultAsync();
        if (profile == null)
        {
            profile = new SecFilerProfile { Cik = cik };
            profileRepository.Add(profile);
        }
        profile.Found = metadata != null;
        profile.Name = Text(metadata?.Name, 512);
        profile.EntityType = Text(metadata?.EntityType, 32);
        profile.Sic = Text(metadata?.Sic, 8);
        profile.SicDescription = Text(metadata?.SicDescription, 256);
        profile.Category = Text(metadata?.Category, 128);
        profile.StateOfIncorporation = Text(metadata?.StateOfIncorporation, 16);
        profile.FetchedAt = DateTime.UtcNow;
        await profileRepository.SaveChanges();
    }

    // EDGAR leaves an unassigned field as "", which is absence, never a value.
    private static string Text(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TruncateToFit(maxLength);
}
