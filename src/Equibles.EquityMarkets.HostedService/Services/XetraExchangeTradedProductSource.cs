using System.Text.Json;
using Equibles.Core.Identity;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Xetra;
using Equibles.Integrations.Xetra.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// Xetra's exchange-traded funds, commodities and notes, read from the same instrument file as its shares and
// reconciled under their own source key; FIRDS states the product and its issuer, the file states the line.
public class XetraExchangeTradedProductSource(XetraInstrumentListClient client)
    : IExchangeTradedProductDirectorySource
{
    public const string Key = "xetra-etp";

    // The file's product types: exchange-traded funds, commodities and notes.
    private static readonly HashSet<string> ProductTypes = new(StringComparer.Ordinal)
    {
        "ETF",
        "ETC",
        "ETN",
    };

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) =>
        market?.DirectorySource == XetraEquityMarketDirectorySource.Key && market.Contains("XETR");

    public async Task<EquityMarketDirectorySnapshot> CaptureProducts(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        var list = await client.GetInstruments(cancellationToken);
        if (!market.Contains(list.MarketIdentifierCode))
            throw new InvalidDataException(
                "Xetra instrument file names a market outside the catalog."
            );
        var lines = list
            .Instruments.Where(row =>
                ProductTypes.Contains(row.InstrumentType) && row.InstrumentStatus == "Active"
            )
            .ToList();
        // A capture states the whole list or throws; a malformed row never degrades into a shorter list.
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in lines)
        {
            if (
                !InternationalSecurityIdentifiers.IsValidIsin(row.Isin)
                || string.IsNullOrWhiteSpace(row.Mnemonic)
                || row.Mnemonic.Length > 32
                || string.IsNullOrWhiteSpace(row.Name)
                || row.Name.Length > 500
            )
                throw new InvalidDataException("Xetra product row has an unexpected shape.");
            if (!symbols.Add(row.Mnemonic))
                throw new InvalidDataException(
                    "Xetra instrument file gives one product symbol multiple security identities."
                );
        }
        // Xetra trades some products in EUR and USD under one ISIN, and reconcile refuses a repeated (ISIN, MIC), so
        // each ISIN keeps its line in the market's currency; one with no single such line is left out and counted.
        var products = new List<XetraInstrument>();
        var excluded = 0;
        foreach (var group in lines.GroupBy(row => (row.Isin, row.MarketIdentifierCode)))
        {
            var line = VenueCurrencyLine(market, group.ToList());
            if (line == null)
                excluded += group.Count();
            else
                products.Add(line);
        }
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = "xetra-all-tradable-instruments-products-v1",
            SourceUrl = list.PageUrl,
            CapturedAt = list.CapturedAt,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    list.PageUrl,
                    list.SourceUrl,
                    list.MarketIdentifierCode,
                    list.LastUpdate,
                    list.CapturedAt,
                    Rows = list.Instruments.Count,
                    Products = lines,
                    Listed = products.Count,
                    Excluded = excluded,
                }
            ),
            Excluded = excluded,
            Rows = products
                .Select(row => new EquityMarketDirectoryRow
                {
                    Isin = row.Isin,
                    MarketIdentifierCode = row.MarketIdentifierCode,
                    Symbol = row.Mnemonic,
                    Name = row.Name,
                    ReportedCurrency = row.Currency,
                    SourceUrl = RowUrl(list.PageUrl, row),
                })
                .ToList(),
        };
    }

    public Task<EquityMarketDirectoryProduct> Resolve(
        EquityMarket market,
        EquityMarketDirectoryRow row,
        FirdsInstrumentRecord firds,
        CancellationToken cancellationToken
    )
    {
        if (firds?.Lei == null)
            throw new InvalidDataException(
                "Xetra rows carry no issuer code; FIRDS must state the product issuer's LEI."
            );
        return Task.FromResult(
            new EquityMarketDirectoryProduct
            {
                SourceIssuerIdentifier = firds.Lei,
                Name = row.Name,
                SourceUrl = row.SourceUrl,
                ReportedCurrency = row.ReportedCurrency,
                Evidence = new
                {
                    row.Isin,
                    row.MarketIdentifierCode,
                    row.Symbol,
                    row.Name,
                    row.ReportedCurrency,
                    FirdsLei = firds.Lei,
                    FirdsCfi = firds.Cfi,
                },
            }
        );
    }

    private static XetraInstrument VenueCurrencyLine(
        EquityMarket market,
        List<XetraInstrument> lines
    )
    {
        if (lines.Count == 1)
            return lines[0];
        var quoted = lines
            .Where(row =>
                EquityQuotationUnits.TryResolve(row.Currency, out var currency, out _)
                && currency == market.Currency
            )
            .ToList();
        return quoted.Count == 1 ? quoted[0] : null;
    }

    // The same stable page-plus-ISIN-and-venue identity the share rows use; the file address rotates daily.
    private static Uri RowUrl(Uri page, XetraInstrument row) =>
        new(page, $"#{row.Isin}-{row.MarketIdentifierCode}");
}
