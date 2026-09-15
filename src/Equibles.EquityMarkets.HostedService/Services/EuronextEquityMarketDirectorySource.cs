using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Euronext;
using Equibles.Integrations.Euronext.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// Euronext's seven directories share one gateway and product-page shape; the catalog code names the slug.
public class EuronextEquityMarketDirectorySource(EuronextDirectoryClient directoryClient)
    : IEquityMarketDirectorySource
{
    public const string Key = "euronext";
    private const string CodePrefix = "euronext-";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) => Market(market) != null;

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        var euronext = Require(market);
        var snapshot = await directoryClient.GetEquities(euronext, cancellationToken);
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = $"euronext-{euronext.Slug}-directory-v1",
            SourceUrl = snapshot.SourceUrl,
            CapturedAt = snapshot.CapturedAt,
            PayloadJson = JsonSerializer.Serialize(snapshot),
            Rows = snapshot
                .Listings.Select(listing => new EquityMarketDirectoryRow
                {
                    Isin = listing.Isin,
                    MarketIdentifierCode = listing.MarketIdentifierCode,
                    Symbol = listing.Symbol,
                    Name = listing.Name,
                    ReportedCurrency = listing.ReportedCurrency,
                    // Euronext links every line to the venue it homes it on; only a sibling venue is stated as the
                    // primary, so a line homed here defers to FIRDS instead of engaging the gate's authority escape.
                    StatedPrimaryMarketIdentifierCode =
                        listing.PrimaryMarketIdentifierCode == listing.MarketIdentifierCode
                            ? null
                            : listing.PrimaryMarketIdentifierCode,
                    SourceUrl = listing.SourceUrl,
                })
                .ToList(),
        };
    }

    public async Task<EquityMarketDirectoryProduct> Resolve(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds,
        CancellationToken cancellationToken
    )
    {
        Require(market);
        var listing = new EuronextEquityListing
        {
            Name = row.Name,
            Isin = row.Isin,
            Symbol = row.Symbol,
            MarketIdentifierCode = row.MarketIdentifierCode,
            ReportedCurrency = row.ReportedCurrency,
            SourceUrl = row.SourceUrl,
        };
        var product = await directoryClient.GetInstrumentIdentity(listing, cancellationToken);
        if (
            product.Isin != row.Isin
            || product.Symbol != row.Symbol
            || product.MarketIdentifierCode != row.MarketIdentifierCode
            || product.SourceInstrumentType != "STOCK"
        )
            throw new InvalidDataException(
                "Euronext product identity conflicts with the directory record."
            );
        return new EquityMarketDirectoryProduct
        {
            SourceIssuerIdentifier = product.IssuerCode,
            Name = product.Name,
            SourceUrl = product.SourceUrl,
            ReportedCurrency = row.ReportedCurrency,
            Evidence = product,
        };
    }

    private static EuronextMarket Market(EquityMarket market) =>
        market?.DirectorySource == Key
        && market.Code.StartsWith(CodePrefix, StringComparison.Ordinal)
            ? EuronextMarket.FromSlug(market.Code[CodePrefix.Length..])
            : null;

    private static EuronextMarket Require(EquityMarket market) =>
        Market(market)
        ?? throw new InvalidOperationException($"{market?.Code} is not a Euronext market.");
}
