using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// The 13F securities that are funds rather than operating companies, from authoritative records
/// only, never a name or ticker: a US listing on the exchange-traded product reference feed
/// (<see cref="EquityListing.IsReferenceListed"/>), and every listing of an issuer whose
/// presentation listing is one or that files Form N-PORT as a registered fund
/// (<see cref="FundSeries"/>, by its issuer link or its registrant CIK). Holder-overlap peers and
/// shareholder targets cover operating companies only.
/// </summary>
public sealed class FundSecurities
{
    /// <summary>No security is a fund.</summary>
    public static readonly FundSecurities None = new([], []);

    private readonly HashSet<HolderSecurityKey> _exchangeTradedListings;
    private readonly HashSet<Guid> _fundIssuers;

    public FundSecurities(
        IEnumerable<HolderSecurityKey> exchangeTradedListings,
        IEnumerable<Guid> fundIssuers
    )
    {
        _exchangeTradedListings = exchangeTradedListings.ToHashSet();
        _fundIssuers = fundIssuers.ToHashSet();
    }

    /// <summary>Whether a security is a fund: its issuer is one, or the listing trades as one.</summary>
    public bool IsFund(HolderSecurityKey security) =>
        _fundIssuers.Contains(security.IssuerId) || _exchangeTradedListings.Contains(security);

    /// <summary>Whether the issuer itself is a fund, so none of its listings is an operating company.</summary>
    public bool IsFundIssuer(Guid issuerId) => _fundIssuers.Contains(issuerId);

    /// <summary>
    /// Classifies <paramref name="issuerIds"/>, or every issuer when null. A listing that is its
    /// issuer's presentation listing makes the issuer a fund; another one marks only itself, so an
    /// operating company's exchange-traded note never makes the company a fund.
    /// </summary>
    public static async Task<FundSecurities> Load(
        IQueryable<EquityIssuer> issuers,
        IQueryable<FundSeries> series,
        IReadOnlyCollection<Guid> issuerIds,
        CancellationToken cancellationToken
    )
    {
        var scoped = issuerIds == null ? issuers : issuers.Where(issuer => issuerIds.Contains(issuer.Id));
        var listings = await scoped
            .SelectMany(issuer =>
                issuer.Securities.SelectMany(security =>
                    security.Listings.Select(listing => new
                    {
                        issuer.Id,
                        listing.Ticker,
                        listing.MarketCountryCode,
                        listing.IsReferenceListed,
                        IsPresentation = issuer.Presentation.EquityListingId == listing.Id,
                    })
                )
            )
            .Where(listing => listing.MarketCountryCode == "US" && listing.IsReferenceListed)
            .ToListAsync(cancellationToken);
        var exchangeTraded = new List<HolderSecurityKey>();
        var fundIssuers = new HashSet<Guid>();
        foreach (var listing in listings)
            if (listing.IsPresentation)
                fundIssuers.Add(listing.Id);
            else
                exchangeTraded.Add(new HolderSecurityKey(listing.Id, listing.Ticker));

        var linked = await series
            .Where(fund =>
                fund.EquityIssuerId != null
                && (issuerIds == null || issuerIds.Contains(fund.EquityIssuerId.Value))
            )
            .Select(fund => fund.EquityIssuerId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        fundIssuers.UnionWith(linked);

        var registrants = (
            await series
                .Where(fund => fund.RegistrantCik != null)
                .Select(fund => fund.RegistrantCik)
                .Distinct()
                .ToListAsync(cancellationToken)
        )
            .Select(CikNormalizer.Canonicalize)
            .Where(cik => !string.IsNullOrEmpty(cik))
            .ToHashSet(StringComparer.Ordinal);
        if (registrants.Count > 0)
        {
            var ciks = await scoped
                .Where(issuer => issuer.Cik != null)
                .Select(issuer => new { issuer.Id, issuer.Cik })
                .ToListAsync(cancellationToken);
            fundIssuers.UnionWith(
                ciks.Where(issuer =>
                        CikNormalizer.Canonicalize(issuer.Cik) is { } cik
                        && registrants.Contains(cik)
                    )
                    .Select(issuer => issuer.Id)
            );
        }
        return new FundSecurities(exchangeTraded, fundIssuers);
    }
}
