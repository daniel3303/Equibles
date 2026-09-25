using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.HostedService.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// The single CUSIP precedence shared by the importer and the stored-label convergence, so a
/// position's stored listing label is always the label an import would write today.
/// </summary>
internal static class HoldingCusipResolution
{
    internal sealed class ListedClaim
    {
        public Guid EquityIssuerId { get; init; }
        public string ListedTicker { get; init; }
        public string Cusip { get; init; }
        public string PresentationTicker { get; init; }
    }

    internal sealed class AliasClaim
    {
        public Guid EquityIssuerId { get; init; }
        public string Cusip { get; init; }
    }

    internal sealed class SecurityClaim
    {
        public Guid Id { get; init; }
        public Guid EquityIssuerId { get; init; }
        public string Cusip { get; init; }
        public Guid? PrimarySecurityId { get; init; }
        public string PresentationTicker { get; init; }
        public List<string> UsTickers { get; init; } = [];
    }

    internal sealed class Claims
    {
        public List<ListedClaim> Listed { get; init; } = [];
        public List<AliasClaim> Aliases { get; init; } = [];
        public List<SecurityClaim> Securities { get; init; } = [];
    }

    /// <summary>
    /// Loads every claim on <paramref name="cusips"/> (every claimed CUSIP when null) made by an
    /// issuer in <paramref name="issuers"/>.
    /// </summary>
    internal static async Task<Claims> Load(
        EquityIssuerRepository stockRepo,
        IQueryable<EquityIssuer> issuers,
        IReadOnlyCollection<string> cusips,
        CancellationToken cancellationToken
    )
    {
        var all = cusips == null;
        var cusipList = cusips?.ToList() ?? [];
        var securities = await issuers
            .SelectMany(issuer => issuer.Securities)
            .Where(security =>
                security.Cusip != null && (all || cusipList.Contains(security.Cusip))
            )
            .Select(security => new SecurityClaim
            {
                Id = security.Id,
                EquityIssuerId = security.EquityIssuerId,
                Cusip = security.Cusip,
                PrimarySecurityId = (Guid?)security.Issuer.Presentation.Listing.EquitySecurityId,
                PresentationTicker = security.Issuer.Presentation.Listing.Ticker,
                UsTickers = security
                    .Listings.Where(listing => listing.MarketCountryCode == "US")
                    .Select(listing => listing.Ticker)
                    .Distinct()
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        var issuerIds = issuers.Select(issuer => issuer.Id);
        var aliases = await stockRepo
            .GetCusipAliases()
            .Where(a =>
                (all || cusipList.Contains(a.Cusip)) && issuerIds.Contains(a.EquityIssuerId)
            )
            .Select(a => new AliasClaim { EquityIssuerId = a.EquityIssuerId, Cusip = a.Cusip })
            .ToListAsync(cancellationToken);

        var listed = await stockRepo
            .GetListedCusips()
            .Where(l =>
                (all || cusipList.Contains(l.Cusip)) && issuerIds.Contains(l.EquityIssuerId)
            )
            .Select(l => new ListedClaim
            {
                EquityIssuerId = l.EquityIssuerId,
                ListedTicker = l.ListedTicker,
                Cusip = l.Cusip,
                PresentationTicker = l.Issuer.Presentation.Listing.Ticker,
            })
            .ToListAsync(cancellationToken);

        return new Claims
        {
            Listed = listed,
            Aliases = aliases,
            Securities = securities,
        };
    }

    /// <summary>
    /// Resolves each claimed CUSIP to its security. The primary CUSIP wins over a retired alias,
    /// which wins over a listing claim; a CUSIP claimed as both an alias and a listing, or by two
    /// securities, is dropped, because resolving it either way would merge two securities.
    /// </summary>
    internal static Dictionary<string, CusipTarget> Resolve(Claims claims, List<string> contested)
    {
        var cusipMapping = new Dictionary<string, CusipTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var listed in claims.Listed)
        {
            // A listing claim naming the presentation listing is that primary series, and a
            // label equal to the presentation ticker would split one security across two keys.
            var listedTicker =
                string.IsNullOrWhiteSpace(listed.ListedTicker)
                || string.Equals(
                    listed.ListedTicker,
                    listed.PresentationTicker,
                    StringComparison.Ordinal
                )
                    ? null
                    : listed.ListedTicker;
            cusipMapping[listed.Cusip] = new CusipTarget(listed.EquityIssuerId, listedTicker);
        }
        var listedClaims = new HashSet<string>(
            claims.Listed.Select(l => l.Cusip),
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var alias in claims.Aliases)
        {
            if (listedClaims.Contains(alias.Cusip))
            {
                contested.Add(alias.Cusip);
                cusipMapping.Remove(alias.Cusip);
                continue;
            }
            cusipMapping[alias.Cusip] = new CusipTarget(alias.EquityIssuerId, null);
        }
        // A retained security keeps its CUSIP after the issuer chooses a different presentation.
        // Ambiguous securities or venue symbols cannot be assigned to the current primary.
        foreach (
            var securityClaims in claims.Securities.GroupBy(
                claim => claim.Cusip,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            cusipMapping.Remove(securityClaims.Key);
            if (securityClaims.Count() != 1)
                continue;
            var security = securityClaims.Single();
            // A retired security trading under the presentation ticker is that primary series
            // before a reverse split or CUSIP change, never a second class.
            if (
                security.Id == security.PrimarySecurityId
                || (
                    security.UsTickers.Count == 1
                    && string.Equals(
                        security.UsTickers[0],
                        security.PresentationTicker,
                        StringComparison.Ordinal
                    )
                )
            )
                cusipMapping[security.Cusip] = new CusipTarget(security.EquityIssuerId, null);
            else if (security.UsTickers.Count == 1)
                cusipMapping[security.Cusip] = new CusipTarget(
                    security.EquityIssuerId,
                    security.UsTickers[0]
                );
        }
        return cusipMapping;
    }
}
