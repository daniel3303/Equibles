using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.Xetra;
using Equibles.Integrations.Xetra.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// Xetra publishes one file that is both the directory and the product; the issuer identity comes from FIRDS.
public class XetraEquityMarketDirectorySource(XetraInstrumentListClient client)
    : IEquityMarketDirectorySource
{
    public const string Key = "xetra";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) =>
        market?.DirectorySource == Key && market.Contains("XETR");

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        var list = await client.GetInstruments(cancellationToken);
        if (!market.Contains(list.MarketIdentifierCode))
            throw new InvalidDataException(
                "Xetra instrument file names a market outside the catalog."
            );
        var shares = list
            .Instruments.Where(row =>
                row.InstrumentType == "CS" && row.InstrumentStatus == "Active"
            )
            .ToList();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in shares)
            if (!symbols.Add(row.Mnemonic))
                throw new InvalidDataException(
                    "Xetra instrument file gives one symbol multiple security identities."
                );
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = "xetra-all-tradable-instruments-v1",
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
                    Shares = shares,
                }
            ),
            Rows = shares
                .Select(row => new EquityMarketDirectoryRow
                {
                    Isin = row.Isin,
                    MarketIdentifierCode = row.MarketIdentifierCode,
                    Symbol = row.Mnemonic,
                    Name = row.Name,
                    ReportedCurrency = row.Currency,
                    StatedPrimaryMarketIdentifierCode = row.PrimaryMarketIdentifierCode,
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
                "Xetra rows carry no issuer code; FIRDS must state the issuer LEI."
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
                    row.StatedPrimaryMarketIdentifierCode,
                    FirdsLei = firds.Lei,
                },
            }
        );
    }

    // The file has no per-row page and its own address rotates daily, so a row is identified by the
    // publisher's stable page plus its ISIN and venue; the file address is kept in the snapshot payload.
    private static Uri RowUrl(Uri page, XetraInstrument row) =>
        new(page, $"#{row.Isin}-{row.MarketIdentifierCode}");
}
