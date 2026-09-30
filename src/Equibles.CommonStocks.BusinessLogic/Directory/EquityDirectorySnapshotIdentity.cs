using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

internal static class EquityDirectorySnapshotIdentity
{
    internal static bool UsesSourceIdentifiers(EquityDirectorySnapshotInput input) =>
        input.Listings.Any(row =>
            row.SourceSecurityIdentifier != null || row.SourceListingIdentifier != null
        );

    internal static bool ValidListings(EquityDirectorySnapshotInput input)
    {
        if (
            input.Listings.Count == 0
            || input.Listings.Any(row =>
                string.IsNullOrWhiteSpace(row.Ticker)
                || !input.MarketIdentifierCodes.Contains(row.MarketIdentifierCode)
            )
        )
            return false;
        if (
            input.Listings.DistinctBy(row => (row.MarketIdentifierCode, row.Ticker)).Count()
            != input.Listings.Count
        )
            return false;
        if (!UsesSourceIdentifiers(input))
            return input.Listings.All(row => !string.IsNullOrWhiteSpace(row.Isin))
                && input.Listings.DistinctBy(row => (row.Isin, row.MarketIdentifierCode)).Count()
                    == input.Listings.Count;
        return input.Listings.All(row =>
                ValidIdentifier(row.SourceSecurityIdentifier)
                && ValidIdentifier(row.SourceListingIdentifier)
                && row.TradingCurrency is { Length: 3 }
            )
            && input.Listings.DistinctBy(row => row.SourceListingIdentifier).Count()
                == input.Listings.Count;
    }

    internal static async Task<HashSet<Guid>> CurrentSourceListings(
        EquityDirectorySnapshotInput input,
        EquityIssuerRepository issuers,
        IReadOnlyList<EquityListing> existing,
        CancellationToken token
    )
    {
        var bindings = await issuers
            .GetListingIdentifiers()
            .Where(row => row.Source == input.Source)
            .Include(row => row.Listing)
                .ThenInclude(row => row.Security)
            .ToDictionaryAsync(row => row.Identifier, token);
        var securities = await issuers
            .GetSecurityIdentifiers()
            .Where(row => row.Source == input.Source)
            .Select(row => new
            {
                row.Identifier,
                row.EquitySecurityId,
                row.Security.Isin,
            })
            .ToDictionaryAsync(row => row.Identifier, token);
        var statedIsins = input
            .Listings.Where(row => row.Isin != null)
            .Select(row => row.Isin)
            .Distinct()
            .ToArray();
        var isinIdentities = await issuers
            .GetSecurities()
            .Where(row => statedIsins.Contains(row.Isin))
            .Select(row => new { row.Isin, row.Id })
            .ToDictionaryAsync(row => row.Isin, token);
        var retained = new HashSet<Guid>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in input.Listings)
        {
            present.Add(row.SourceListingIdentifier);
            if (
                securities.TryGetValue(row.SourceSecurityIdentifier, out var boundSecurity)
                && row.Isin != null
                && (
                    boundSecurity.Isin != null && row.Isin != boundSecurity.Isin
                    || isinIdentities.TryGetValue(row.Isin, out var byIsin)
                        && byIsin.Id != boundSecurity.EquitySecurityId
                )
            )
                throw new InvalidDataException(
                    "Directory snapshot conflicts with its security ISIN."
                );
            if (!bindings.TryGetValue(row.SourceListingIdentifier, out var binding))
                continue;
            var listing = binding.Listing;
            if (
                !securities.TryGetValue(row.SourceSecurityIdentifier, out var security)
                || security.EquitySecurityId != listing.EquitySecurityId
            )
                throw new InvalidDataException(
                    "Directory snapshot conflicts with its instrument identifiers."
                );
            if (!MatchesInstrument(row, listing) || listing.DelistedOn != null)
                throw new InvalidDataException(
                    "Directory snapshot conflicts with its instrument quotation identity."
                );
            // A rename is applied by the subsequent row import, preserving the listing's stable identity.
            if (row.Ticker == listing.Ticker)
                retained.Add(listing.Id);
        }
        foreach (
            var binding in bindings.Values.Where(binding =>
                binding.Listing.MarketCountryCode == input.MarketCountryCode
                && input.MarketIdentifierCodes.Contains(binding.Listing.MarketIdentifierCode)
            )
        )
            binding.IsDirectoryListed = present.Contains(binding.Identifier);
        var independentlyListed = await issuers
            .GetListingIdentifiers()
            .Where(row => row.Source != input.Source && row.IsDirectoryListed)
            .Select(row => row.EquityListingId)
            .ToListAsync(token);
        retained.UnionWith(independentlyListed);
        // Identities never owned by this source remain outside its withdrawal authority.
        var owned = bindings.Values.Select(row => row.EquityListingId).ToHashSet();
        retained.UnionWith(existing.Where(row => !owned.Contains(row.Id)).Select(row => row.Id));
        return retained;
    }

    private static bool MatchesInstrument(EquityDirectoryListingKey row, EquityListing listing) =>
        row.MarketIdentifierCode == listing.MarketIdentifierCode
        && row.TradingCurrency == listing.TradingCurrency
        && (row.Isin == null || listing.Security.Isin == null || row.Isin == listing.Security.Isin);

    private static bool ValidIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim();
}
