using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Lse;
using Equibles.Integrations.Lse.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// London publishes one workbook that is both the directory and the product; the issuer identity comes from FIRDS.
public class LseEquityMarketDirectorySource(LseInstrumentListClient client)
    : IEquityMarketDirectorySource
{
    public const string Key = "lse";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) =>
        market?.DirectorySource == Key && market.Contains("XLON");

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        var list = await client.GetInstruments(cancellationToken);
        var shares = list
            .Instruments.Where(row =>
                row.MifirIdentifier == "SHRS"
                && market.Contains(row.MarketIdentifierCode)
                && !string.IsNullOrWhiteSpace(row.Isin)
            )
            .ToList();
        var rows = new List<EquityMarketDirectoryRow>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (
            var line in shares
                .GroupBy(row => (row.Isin, row.MarketIdentifierCode))
                .Select(group => Primary(market, group.ToList()))
                .Where(line => line != null)
        )
        {
            var symbol = EquityMarketDirectorySymbol.Normalize(line.Tidm);
            if (symbol == null)
                continue;
            if (!symbols.Add(line.MarketIdentifierCode + ":" + symbol))
                throw new InvalidDataException(
                    "London instrument list gives one symbol multiple security identities."
                );
            rows.Add(
                new EquityMarketDirectoryRow
                {
                    Isin = line.Isin,
                    MarketIdentifierCode = line.MarketIdentifierCode,
                    Symbol = symbol,
                    Name = line.IssuerName,
                    ReportedCurrency = line.TradingCurrency,
                    SourceUrl = RowUrl(list.PageUrl, line),
                }
            );
        }
        if (rows.Count == 0)
            throw new InvalidDataException(
                "London instrument list names no shares of this market."
            );
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = "lse-instrument-list-v1",
            SourceUrl = list.PageUrl,
            CapturedAt = list.CapturedAt,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    list.PageUrl,
                    list.SourceUrl,
                    list.Edition,
                    AsAt = list.AsAt.ToString("yyyy-MM-dd"),
                    list.StatedCount,
                    list.CapturedAt,
                    Rows = list.Instruments.Count,
                    Shares = shares,
                    Listed = rows.Count,
                }
            ),
            Rows = rows,
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
                "London rows carry no issuer code; FIRDS must state the issuer LEI."
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
                },
            }
        );
    }

    // One security can hold several lines on one venue, a currency line per quotation and a line per share
    // register. The line quoted in the venue's own currency is the one its market data and the price provider
    // mean; when none or several are, the security is left out rather than resolved by preference.
    private static LseInstrument Primary(EquityMarket market, List<LseInstrument> lines)
    {
        if (lines.Count == 1)
            return lines[0];
        var quoted = lines
            .Where(row =>
                EquityQuotationUnits.TryResolve(row.TradingCurrency, out var currency, out _)
                && currency == market.Currency
            )
            .ToList();
        return quoted.Count == 1 ? quoted[0] : null;
    }

    // The workbook has no per-row page and its address carries an edition number, so a row is identified by the
    // publisher's stable page plus its ISIN and venue; the edition is kept in the snapshot payload.
    private static Uri RowUrl(Uri page, LseInstrument row) =>
        new(page, $"#{row.Isin}-{row.MarketIdentifierCode}");
}
