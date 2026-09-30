using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.CommonStocks.BusinessLogic.Directory;

[Service]
public class EquityDirectoryIdentityImporter(IServiceScopeFactory scopeFactory)
{
    public virtual async Task<Guid> ImportListing(
        EquityDirectoryListingInput input,
        CancellationToken cancellationToken = default
    )
    {
        EquityDirectoryListingInputValidator.Validate(input);
        // Failed identity writes discard their complete tracked graph; no later save can leak it.
        await using var scope = scopeFactory.CreateAsyncScope();
        var issuers = scope.ServiceProvider.GetRequiredService<EquityIssuerRepository>();
        var listings = scope.ServiceProvider.GetRequiredService<EquityListingRepository>();
        var evidence =
            scope.ServiceProvider.GetRequiredService<EquityDirectorySourceRecordRepository>();
        await using var transaction = await issuers.BeginDirectoryIdentityWrite(cancellationToken);
        if (transaction == null)
            throw new InvalidOperationException(
                "Directory imports require an independent relational transaction."
            );
        if (
            await evidence
                .GetSnapshotStates()
                .AnyAsync(row => row.Source == input.Source, cancellationToken)
            && (
                !input.DirectorySnapshotId.HasValue
                || !await evidence
                    .GetSnapshotStates()
                    .AnyAsync(
                        row =>
                            row.Source == input.Source
                            && row.SourceRecordId == input.DirectorySnapshotId.Value,
                        cancellationToken
                    )
            )
        )
            throw new InvalidDataException(
                "The listing input belongs to a superseded directory snapshot."
            );
        var sourceIdentifier = await issuers
            .GetSourceIdentifiers()
            .SingleOrDefaultAsync(
                row => row.Source == input.Source && row.Identifier == input.SourceIssuerIdentifier,
                cancellationToken
            );
        var instruments = await EquityDirectoryInstrumentIdentity.Read(
            issuers,
            input,
            cancellationToken
        );
        var issuer = await ResolveIssuer(
            issuers,
            input,
            sourceIdentifier,
            instruments.IssuerIds,
            cancellationToken
        );
        if (issuer == null)
        {
            issuer = new EquityIssuer
            {
                Name = input.IssuerName,
                LegalEntityIdentifier = input.LegalEntityIdentifier,
                IdentitySourceUrl = input.SourceUrl,
            };
            issuers.Add(issuer);
        }
        else
        {
            await issuers.LockIssuerForDirectoryWrite(issuer.Id, cancellationToken);
            if (
                issuer.LegalEntityIdentifier != null
                && input.LegalEntityIdentifier != null
                && issuer.LegalEntityIdentifier != input.LegalEntityIdentifier
            )
                throw new InvalidDataException(
                    "Directory source conflicts with the issuer's legal identity."
                );
            issuer.LegalEntityIdentifier ??= input.LegalEntityIdentifier;
            if (string.IsNullOrWhiteSpace(issuer.Name))
                issuer.Name = input.IssuerName;
        }
        var security = instruments.Security(issuer);
        var listing = instruments.Listing(security);
        var existingListingId = listing?.Id ?? Guid.Empty;
        if (
            await listings
                .GetAll()
                .AnyAsync(
                    row =>
                        row.Active
                        && row.MarketIdentifierCode == input.MarketIdentifierCode
                        && row.Ticker == input.Ticker
                        && row.Id != existingListingId,
                    cancellationToken
                )
        )
            throw new InvalidDataException(
                "Directory symbol is already owned by another listing on this venue."
            );
        if (listing == null)
        {
            listing = new EquityListing
            {
                Security = security,
                MarketIdentifierCode = input.MarketIdentifierCode,
                MarketCountryCode = input.MarketCountryCode,
            };
            security.Listings.Add(listing);
        }
        if (
            listing.MarketCountryCode != null
                && listing.MarketCountryCode != input.MarketCountryCode
            || listing.TradingCurrency != null
                && input.TradingCurrency != null
                && listing.TradingCurrency != input.TradingCurrency
            || listing.QuoteUnitMultiplier != null
                && input.QuoteUnitMultiplier != null
                && listing.QuoteUnitMultiplier != input.QuoteUnitMultiplier
        )
            throw new InvalidDataException(
                "Directory quotation units conflict with the existing listing."
            );
        listing.MarketCountryCode ??= input.MarketCountryCode;
        listing.Ticker = input.Ticker;
        listing.TradingCurrency ??= input.TradingCurrency;
        listing.QuoteUnitMultiplier ??= input.QuoteUnitMultiplier;
        listing.Active = true;
        listing.IsDirectoryListed = true;
        listing.IdentitySourceUrl = input.SourceUrl;
        if (listing.TradingCurrency != null && listing.QuoteUnitMultiplier != null)
            listing.IdentityState = EquityIdentityState.Verified;
        issuer.Presentation ??= new EquityIssuerPresentation { Issuer = issuer, Listing = listing };
        var recordId = await evidence.Append(
            input.Source,
            input.SourceUrl,
            input.PayloadJson,
            cancellationToken
        );
        if (sourceIdentifier == null)
            issuers.AddSourceIdentifier(
                new EquityIssuerSourceIdentifier
                {
                    Issuer = issuer,
                    Source = input.Source,
                    Identifier = input.SourceIssuerIdentifier,
                    SourceRecordId = recordId,
                }
            );
        instruments.Bind(issuers, listing, recordId);
        await issuers.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
        return listing.Id;
    }

    private static async Task<EquityIssuer> ResolveIssuer(
        EquityIssuerRepository repository,
        EquityDirectoryListingInput input,
        EquityIssuerSourceIdentifier sourceIdentifier,
        IEnumerable<Guid> instrumentIssuerIds,
        CancellationToken cancellationToken
    )
    {
        var candidates = new HashSet<Guid>(instrumentIssuerIds);
        if (sourceIdentifier != null)
            candidates.Add(sourceIdentifier.EquityIssuerId);
        if (input.LegalEntityIdentifier != null)
            candidates.UnionWith(
                await repository
                    .GetByLegalEntityIdentifier(input.LegalEntityIdentifier)
                    .Select(row => row.Id)
                    .ToListAsync(cancellationToken)
            );
        var related =
            input.LegalEntityIdentifier == null
                ? input.Isin == null
                    ? new List<string>()
                    : new List<string> { input.Isin }
                : input.RelatedIsins;
        // ISO 6166 embeds the national CUSIP only in US/CA ISINs; this never classifies the security.
        var cusips = related
            .Where(isin =>
                isin.StartsWith("US", StringComparison.Ordinal)
                || isin.StartsWith("CA", StringComparison.Ordinal)
            )
            .Select(isin => isin.Substring(2, 9))
            .Distinct()
            .ToList();
        candidates.UnionWith(
            await repository
                .GetSecurities()
                .Where(row =>
                    input.Isin != null && row.Isin == input.Isin
                    || input.LegalEntityIdentifier != null
                        && (
                            row.Isin != null && related.Contains(row.Isin)
                            || row.Cusip != null
                                && cusips.Contains(row.Cusip)
                                && row.Listings.Any(listing =>
                                    listing.Active && listing.MarketCountryCode == "US"
                                )
                        )
                )
                .Select(row => row.EquityIssuerId)
                .Distinct()
                .ToListAsync(cancellationToken)
        );
        if (candidates.Count > 1)
            throw new InvalidDataException(
                "Directory identifiers resolve to conflicting issuers; refusing an inferred merge."
            );
        return candidates.Count == 0 ? null : await repository.Get(candidates.Single());
    }
}
