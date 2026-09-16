using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Gpw;
using Equibles.Integrations.Gpw.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// The main market's quotation tables are the directory, the company page is the product, and the issuer
// identity comes from FIRDS.
public class GpwEquityMarketDirectorySource(GpwClient client) : IEquityMarketDirectorySource
{
    public const string Key = "gpw";
    private const string MarketIdentifierCode = "XWAR";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) =>
        market?.DirectorySource == Key && market.Contains(MarketIdentifierCode);

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        if (!Supports(market))
            throw new InvalidOperationException($"{market?.Code} is not the GPW market.");
        var list = await client.GetQuotations(cancellationToken);
        var rows = new List<EquityMarketDirectoryRow>();
        var isins = new HashSet<string>(StringComparer.Ordinal);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quotation in list.Quotations)
        {
            if (!market.Contains(quotation.MarketIdentifierCode))
                throw new InvalidDataException(
                    "GPW quotations table names a venue outside the market."
                );
            var symbol =
                EquityMarketDirectorySymbol.Normalize(quotation.Shortcut)
                ?? throw new InvalidDataException(
                    "GPW quotations table states a shortcut outside the listing contract."
                );
            if (!isins.Add(quotation.Isin))
                throw new InvalidDataException("GPW quotations tables repeat a share identity.");
            if (!symbols.Add(symbol))
                throw new InvalidDataException(
                    "GPW quotations tables give one shortcut multiple security identities."
                );
            rows.Add(
                new EquityMarketDirectoryRow
                {
                    Isin = quotation.Isin,
                    MarketIdentifierCode = quotation.MarketIdentifierCode,
                    Symbol = symbol,
                    Name = quotation.Name,
                    ReportedCurrency = quotation.Currency,
                    StatedPrimaryMarketIdentifierCode = null,
                    SourceUrl = GpwClient.FactsheetUrl(quotation.Isin),
                }
            );
        }
        if (rows.Count == 0)
            throw new InvalidDataException("GPW quotations tables list no share.");
        // The continuous-trading table is the snapshot's address; the auction tables are kept in the payload.
        var continuous = list.Tables[0];
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = "gpw-main-market-quotations-v1",
            SourceUrl = continuous.SourceUrl,
            CapturedAt = list.CapturedAt,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    list.CapturedAt,
                    Tables = list.Tables.Select(table => new
                    {
                        table.TradingSystem,
                        table.SourceUrl,
                        Rows = table.Quotations.Count,
                        table.Quotations,
                    }),
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
            throw new InvalidOperationException($"{market?.Code} is not the GPW market.");
        if (firds?.Lei == null)
            throw new InvalidDataException(
                "GPW rows carry no issuer code; FIRDS must state the issuer LEI."
            );
        var factsheet = await client.GetFactsheet(row.Isin, cancellationToken);
        if (
            factsheet.Isin != row.Isin
            || factsheet.SourceUrl != row.SourceUrl
            || EquityMarketDirectorySymbol.Normalize(factsheet.Shortcut) != row.Symbol
        )
            throw new InvalidDataException("GPW company page conflicts with the directory record.");
        return new EquityMarketDirectoryProduct
        {
            SourceIssuerIdentifier = firds.Lei,
            Name = factsheet.Name,
            SourceUrl = factsheet.SourceUrl,
            ReportedCurrency = row.ReportedCurrency,
            Evidence = new
            {
                factsheet.Name,
                factsheet.Isin,
                factsheet.Shortcut,
                TableName = row.Name,
                row.MarketIdentifierCode,
                row.ReportedCurrency,
                FirdsLei = firds.Lei,
            },
        };
    }
}
