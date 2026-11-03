using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Repositories;

/// <summary>
/// Each current filer's other active, directory-listed US line (a second share class, a
/// preferred, a warrant) mapped to its exact listing, for sources that report every line under
/// its own symbol. The FINRA importers and the readers that reproduce their universe share it.
/// Callers match these keys by exact or dotted spelling only: no authoritative security type
/// tells the lines apart, and a compressed preferred spelling can be another company's symbol.
/// A ticker already claimed, or held by any other active US line, is dropped.
/// </summary>
public static class SecondaryListingClaims
{
    public static async Task<Dictionary<string, EquityListingReference>> Build(
        EquityIssuerRepository issuerRepository,
        EquityListingRepository listingRepository,
        ICollection<string> claimedTickers,
        IReadOnlyCollection<string> tickersToSync,
        StringComparer comparer,
        CancellationToken cancellationToken
    )
    {
        var presentations = await issuerRepository
            .GetCurrentUsDirectory()
            .AsNoTracking()
            .Where(issuer => issuer.Presentation.Listing.Active)
            .Select(issuer => new
            {
                issuer.Id,
                issuer.Presentation.EquityListingId,
                issuer.Presentation.Listing.Ticker,
            })
            .ToDictionaryAsync(issuer => issuer.Id, cancellationToken);
        var activeUsLines = await listingRepository
            .GetActiveUsListings()
            .AsNoTracking()
            .Where(listing => listing.Ticker != null && listing.Ticker != "")
            .Select(listing => new
            {
                listing.Id,
                listing.Security.EquityIssuerId,
                listing.Ticker,
                listing.IsDirectoryListed,
                listing.IsReferenceListed,
            })
            .ToListAsync(cancellationToken);
        var delisted = (
            await issuerRepository
                .GetDelistedListings()
                .AsNoTracking()
                .Select(listing => new { listing.EquityIssuerId, listing.ListedTicker })
                .ToListAsync(cancellationToken)
        )
            .Select(listing => new ListedSecurityKey(
                listing.EquityIssuerId,
                TickerNormalizer.NormalizeDashListed(listing.ListedTicker)
            ))
            .ToHashSet();
        // Compared by the dash spelling, case preserved: a dotted twin of a held ticker is the
        // same symbol to a source that writes dots, while letter case stays the caller's choice.
        var linesPerSpelling = activeUsLines
            .GroupBy(line => DashSpelling(line.Ticker), comparer)
            .ToDictionary(group => group.Key, group => group.Count(), comparer);
        var claimedSpellings = claimedTickers.Select(DashSpelling).ToHashSet(comparer);
        var requested = tickersToSync?.Count > 0 ? tickersToSync.ToHashSet(comparer) : null;
        return activeUsLines
            .Where(line =>
                line.IsDirectoryListed
                && !line.IsReferenceListed
                && presentations.TryGetValue(line.EquityIssuerId, out var presentation)
                && line.Id != presentation.EquityListingId
                && !string.Equals(
                    TickerNormalizer.NormalizeDashListed(line.Ticker),
                    TickerNormalizer.NormalizeDashListed(presentation.Ticker),
                    StringComparison.OrdinalIgnoreCase
                )
                && linesPerSpelling[DashSpelling(line.Ticker)] == 1
                && !claimedSpellings.Contains(DashSpelling(line.Ticker))
                && (requested == null || requested.Contains(line.Ticker))
                && !delisted.Contains(
                    new ListedSecurityKey(
                        line.EquityIssuerId,
                        TickerNormalizer.NormalizeDashListed(line.Ticker)
                    )
                )
            )
            .ToDictionary(
                line => line.Ticker,
                line => new EquityListingReference(line.Id, line.EquityIssuerId, line.Ticker),
                comparer
            );
    }

    private static string DashSpelling(string ticker) => ticker.Replace('.', '-');
}
