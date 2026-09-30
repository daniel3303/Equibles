using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

internal sealed class EquityDirectoryInstrumentIdentity(
    EquityDirectoryListingInput input,
    EquitySecuritySourceIdentifier securityIdentifier,
    EquityListingSourceIdentifier listingIdentifier
)
{
    public IEnumerable<Guid> IssuerIds
    {
        get
        {
            if (securityIdentifier != null)
                yield return securityIdentifier.Security.EquityIssuerId;
            if (listingIdentifier != null)
                yield return listingIdentifier.Listing.Security.EquityIssuerId;
        }
    }

    public static async Task<EquityDirectoryInstrumentIdentity> Read(
        EquityIssuerRepository repository,
        EquityDirectoryListingInput input,
        CancellationToken token
    )
    {
        if (input.SourceSecurityIdentifier == null)
            return new(input, null, null);
        var security = await repository
            .GetSecurityIdentifiers()
            .Include(row => row.Security)
            .SingleOrDefaultAsync(
                row =>
                    row.Source == input.Source && row.Identifier == input.SourceSecurityIdentifier,
                token
            );
        var listing = await repository
            .GetListingIdentifiers()
            .Include(row => row.Listing)
                .ThenInclude(row => row.Security)
            .SingleOrDefaultAsync(
                row =>
                    row.Source == input.Source && row.Identifier == input.SourceListingIdentifier,
                token
            );
        return new(input, security, listing);
    }

    public EquitySecurity Security(EquityIssuer issuer)
    {
        var byIsin =
            input.Isin == null
                ? null
                : issuer.Securities.SingleOrDefault(row => row.Isin == input.Isin);
        var security = securityIdentifier?.Security ?? byIsin;
        if (
            security != null
            && (
                security.EquityIssuerId != issuer.Id
                || byIsin != null && byIsin.Id != security.Id
                || security.Isin != null && input.Isin != null && security.Isin != input.Isin
            )
        )
            throw new InvalidDataException(
                "Directory security identifiers conflict with the recorded instrument."
            );
        if (security == null)
        {
            security = new EquitySecurity { Issuer = issuer, IdentitySourceUrl = input.SourceUrl };
            issuer.Securities.Add(security);
        }
        security.Isin ??= input.Isin;
        return security;
    }

    public EquityListing Listing(EquitySecurity security)
    {
        if (listingIdentifier != null)
        {
            var bound = listingIdentifier.Listing;
            if (
                bound.EquitySecurityId != security.Id
                || bound.MarketIdentifierCode != input.MarketIdentifierCode
                || bound.DelistedOn != null
            )
                throw new InvalidDataException(
                    "Directory listing identifier conflicts with its security or venue history."
                );
            return bound;
        }
        var candidates = security
            .Listings.Where(row =>
                row.MarketIdentifierCode == input.MarketIdentifierCode
                && (row.Active || input.DirectorySnapshotId.HasValue && row.DelistedOn == null)
                && (input.SourceListingIdentifier == null || row.Ticker == input.Ticker)
            )
            .ToList();
        if (candidates.Count > 1)
            throw new InvalidDataException(
                "Directory security has multiple eligible listing identities on this venue."
            );
        return candidates.SingleOrDefault();
    }

    public void Bind(EquityIssuerRepository repository, EquityListing listing, Guid recordId)
    {
        if (input.SourceSecurityIdentifier == null)
            return;
        if (securityIdentifier == null)
            repository.AddSecurityIdentifier(
                new EquitySecuritySourceIdentifier
                {
                    Security = listing.Security,
                    Source = input.Source,
                    Identifier = input.SourceSecurityIdentifier,
                    SourceRecordId = recordId,
                }
            );
        if (listingIdentifier == null)
            repository.AddListingIdentifier(
                new EquityListingSourceIdentifier
                {
                    Listing = listing,
                    Source = input.Source,
                    Identifier = input.SourceListingIdentifier,
                    SourceRecordId = recordId,
                }
            );
    }
}
