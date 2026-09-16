using System.Text.Json;
using Equibles.EquityMarkets.BusinessLogic.Directory;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.Integrations.NasdaqNordic;
using Equibles.Integrations.NasdaqNordic.Models;

namespace Equibles.EquityMarkets.HostedService.Services;

// Each Nordic exchange publishes a main-market and a First North list; the list a line comes from names its
// venue, the instrument reply confirms the line, and the issuer identity comes from FIRDS.
public class NasdaqNordicEquityMarketDirectorySource(NasdaqNordicClient client)
    : IEquityMarketDirectorySource
{
    public const string Key = "nasdaq-nordic";
    private const string CodePrefix = "nasdaq-";

    public string SourceKey => Key;

    public bool Supports(EquityMarket market) => Market(market) != null;

    public async Task<EquityMarketDirectorySnapshot> Capture(
        EquityMarket market,
        CancellationToken cancellationToken
    )
    {
        var nasdaq = Require(market);
        var lists = new List<NasdaqNordicShareList>();
        foreach (var category in NasdaqNordicMarket.Categories)
            lists.Add(await client.GetShares(nasdaq, category, cancellationToken));
        var rows = new List<EquityMarketDirectoryRow>();
        var identities = new HashSet<(string Isin, string Mic)>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            var mic = nasdaq.MarketIdentifierCode(list.Category);
            foreach (var share in list.Shares)
            {
                var symbol =
                    EquityMarketDirectorySymbol.Normalize(share.Symbol)
                    ?? throw new InvalidDataException(
                        "Nasdaq screener states a symbol outside the listing contract."
                    );
                if (!identities.Add((share.Isin, mic)))
                    throw new InvalidDataException(
                        "Nasdaq screener lists repeat a share identity."
                    );
                if (!symbols.Add(symbol))
                    throw new InvalidDataException(
                        "Nasdaq screener lists give one symbol multiple security identities."
                    );
                rows.Add(
                    new EquityMarketDirectoryRow
                    {
                        Isin = share.Isin,
                        MarketIdentifierCode = mic,
                        Symbol = symbol,
                        Name = share.FullName,
                        ReportedCurrency = share.Currency,
                        StatedPrimaryMarketIdentifierCode = null,
                        SourceUrl = NasdaqNordicClient.InstrumentUrl(share.OrderbookId),
                    }
                );
            }
        }
        // The main-market list is the snapshot's address; the First North list is kept in the payload.
        return new EquityMarketDirectorySnapshot
        {
            EvidenceSource = $"nasdaq-nordic-{nasdaq.Slug}-screener-v1",
            SourceUrl = lists[0].SourceUrl,
            CapturedAt = lists[0].CapturedAt,
            PayloadJson = JsonSerializer.Serialize(
                new
                {
                    Market = nasdaq.MarketCode,
                    Lists = lists.Select(list => new
                    {
                        list.SourceUrl,
                        Category = list.Category.ToString(),
                        MarketIdentifierCode = nasdaq.MarketIdentifierCode(list.Category),
                        list.CapturedAt,
                        Rows = list.Shares.Count,
                        list.Shares,
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
        var nasdaq = Require(market);
        if (firds?.Lei == null)
            throw new InvalidDataException(
                "Nasdaq rows carry no issuer code; FIRDS must state the issuer LEI."
            );
        var category =
            nasdaq.CategoryOf(row.MarketIdentifierCode)
            ?? throw new InvalidDataException("Nasdaq row names a venue outside its market.");
        var instrument = await client.GetInstrument(row.SourceUrl, cancellationToken);
        if (
            instrument.Isin != row.Isin
            || EquityMarketDirectorySymbol.Normalize(instrument.Symbol) != row.Symbol
            || !nasdaq
                .ExchangeLabels(category)
                .Contains(instrument.Exchange, StringComparer.Ordinal)
            || instrument.Currency != row.ReportedCurrency
        )
            throw new InvalidDataException(
                "Nasdaq instrument identity conflicts with the directory record: the reply states "
                    + $"{instrument.Isin} {EquityMarketDirectorySymbol.Normalize(instrument.Symbol) ?? instrument.Symbol} "
                    + $"{instrument.Currency} on \"{instrument.Exchange}\" where the row states "
                    + $"{row.Isin} {row.Symbol} {row.ReportedCurrency} on "
                    + $"\"{string.Join("\" or \"", nasdaq.ExchangeLabels(category))}\"."
            );
        return new EquityMarketDirectoryProduct
        {
            SourceIssuerIdentifier = firds.Lei,
            Name = instrument.CompanyName,
            SourceUrl = row.SourceUrl,
            ReportedCurrency = instrument.Currency,
            Evidence = new
            {
                instrument.Symbol,
                instrument.CompanyName,
                instrument.Exchange,
                instrument.Segment,
                instrument.MarketStatus,
                instrument.Isin,
                instrument.Currency,
                row.MarketIdentifierCode,
                FirdsLei = firds.Lei,
            },
        };
    }

    private static NasdaqNordicMarket Market(EquityMarket market) =>
        market?.DirectorySource == Key
        && market.Code.StartsWith(CodePrefix, StringComparison.Ordinal)
        && NasdaqNordicMarket.FromSlug(market.Code[CodePrefix.Length..]) is { } nasdaq
        && market.Contains(nasdaq.MainMarketIdentifierCode)
        && market.Contains(nasdaq.FirstNorthIdentifierCode)
            ? nasdaq
            : null;

    private static NasdaqNordicMarket Require(EquityMarket market) =>
        Market(market)
        ?? throw new InvalidOperationException($"{market?.Code} is not a Nasdaq Nordic market.");
}
