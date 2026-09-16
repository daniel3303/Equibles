using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Bme;
using Equibles.Integrations.Bme.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// The continuous market lists one main line per company; the ticker, the currency and any further current
// line of the same issuer come from each line's own details reply, which is also the row's product.
public class BmeEquityMarketDirectorySource(BmeClient client) : IEquityMarketDirectorySource
{
    public const string Key = "bme";
    private const string MarketIdentifierCode = "XMAD";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) =>
        market?.DirectorySource == Key && market.Contains(MarketIdentifierCode);

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        if (!Supports(market))
            throw new InvalidOperationException($"{market?.Code} is not the BME market.");
        var companies = await client.GetListedCompanies(cancellationToken);
        var lines = new List<BmeShareDetails>();
        var isins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var company in companies.Companies)
        {
            var details = await client.GetShareDetails(company.Isin, cancellationToken);
            if (details.TradingSystem != BmeParser.ContinuousMarket)
                throw new InvalidDataException(
                    "BME listed company is not admitted to the continuous market."
                );
            if (isins.Add(details.Isin))
                lines.Add(details);
            // A second share class is a line of its own; a cancelled line and one admitted elsewhere are not. A
            // live subscription-rights line is read too and the FIRDS share gate leaves it out by its CFI.
            foreach (var other in details.OtherSharesFromIssuer.Where(other => other.IsCurrent))
            {
                if (!isins.Add(other.Isin))
                    continue;
                var line = await client.GetShareDetails(other.Isin, cancellationToken);
                if (line.TradingSystem == BmeParser.ContinuousMarket)
                    lines.Add(line);
            }
        }
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<EquityMarketDirectoryRow>();
        foreach (var line in lines)
        {
            var symbol =
                EquityMarketDirectorySymbol.Normalize(line.Ticker)
                ?? throw new InvalidDataException(
                    "BME share details state a ticker outside the listing contract."
                );
            if (!symbols.Add(symbol))
                throw new InvalidDataException(
                    "BME share details give one ticker multiple security identities."
                );
            rows.Add(
                new EquityMarketDirectoryRow
                {
                    Isin = line.Isin,
                    MarketIdentifierCode = MarketIdentifierCode,
                    Symbol = symbol,
                    Name = line.Name,
                    ReportedCurrency = line.Currency,
                    StatedPrimaryMarketIdentifierCode = null,
                    SourceUrl = line.SourceUrl,
                }
            );
        }
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = "bme-continuous-market-listed-companies-v1",
            SourceUrl = companies.SourceUrl,
            CapturedAt = companies.CapturedAt,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    companies.SourceUrl,
                    companies.CapturedAt,
                    companies.TotalResults,
                    companies.Companies,
                    Shares = lines.Select(Evidence),
                }
            ),
            Rows = rows,
        };
    }

    public async Task<EquityMarketDirectoryProduct> Resolve(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds,
        CancellationToken cancellationToken
    )
    {
        if (!Supports(market))
            throw new InvalidOperationException($"{market?.Code} is not the BME market.");
        var details = await client.GetShareDetails(row.Isin, cancellationToken);
        if (
            details.Isin != row.Isin
            || details.SourceUrl != row.SourceUrl
            || EquityMarketDirectorySymbol.Normalize(details.Ticker) != row.Symbol
            || details.TradingSystem != BmeParser.ContinuousMarket
            || details.Currency != row.ReportedCurrency
        )
            throw new InvalidDataException("BME share details conflict with the directory record.");
        return new EquityMarketDirectoryProduct
        {
            SourceIssuerIdentifier = details.IssuerCode,
            Name = details.Name,
            SourceUrl = details.SourceUrl,
            ReportedCurrency = details.Currency,
            Evidence = Evidence(details),
        };
    }

    private static object Evidence(BmeShareDetails details) =>
        new
        {
            details.Name,
            details.ShortName,
            details.Isin,
            details.Ticker,
            details.IssuerCode,
            details.Market,
            details.TradingSystem,
            details.Currency,
            details.Active,
            details.OtherSharesFromIssuer,
            details.SourceUrl,
        };
}
