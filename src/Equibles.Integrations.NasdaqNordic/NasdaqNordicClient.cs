using Equibles.Integrations.Common.Http;
using Equibles.Integrations.NasdaqNordic.Models;

namespace Equibles.Integrations.NasdaqNordic;

// Plain GETs against the venue's public data API; both replies are read whole and bounded.
public class NasdaqNordicClient(HttpClient httpClient)
{
    private static readonly Uri Origin = new("https://api.nasdaq.com");
    private const string Accept = "application/json";
    private const int MaxListBytes = 8_000_000;
    private const int MaxInstrumentBytes = 1_000_000;

    public static Uri ShareListUrl(NasdaqNordicMarket market, NasdaqNordicCategory category) =>
        new(
            Origin,
            $"/api/nordic/screener/shares?category={CategoryParameter(category)}&market={market.MarketCode}&tableonly=false"
        );

    public static Uri InstrumentUrl(string orderbookId) =>
        new(Origin, $"/api/nordic/instruments/{orderbookId}/info?assetClass=SHARES");

    // The instrument address is orderbook-keyed; only an address this client would compose is fetched.
    public static bool IsInstrumentUrl(Uri uri) =>
        uri != null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == Origin.Host
        && uri.IsDefaultPort
        && uri.Query == "?assetClass=SHARES"
        && uri.AbsolutePath.StartsWith("/api/nordic/instruments/", StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith("/info", StringComparison.Ordinal)
        && uri.Segments.Length == 6;

    public async Task<NasdaqNordicShareList> GetShares(
        NasdaqNordicMarket market,
        NasdaqNordicCategory category,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(market);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var url = ShareListUrl(market, category);
        var json = await SameOriginTextReader.Read(
            httpClient,
            Origin,
            url,
            MaxListBytes,
            timeout.Token,
            Accept
        );
        var list = NasdaqNordicParser.ReadShareList(json);
        list.SourceUrl = url;
        list.MarketCode = market.MarketCode;
        list.Category = category;
        list.CapturedAt = DateTime.UtcNow;
        return list;
    }

    public async Task<NasdaqNordicInstrument> GetInstrument(
        Uri instrumentUrl,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsInstrumentUrl(instrumentUrl))
            throw new InvalidDataException(
                "Nasdaq instrument address is not one this client composes."
            );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var json = await SameOriginTextReader.Read(
            httpClient,
            Origin,
            instrumentUrl,
            MaxInstrumentBytes,
            timeout.Token,
            Accept
        );
        var instrument = NasdaqNordicParser.ReadInstrument(json);
        instrument.SourceUrl = instrumentUrl;
        return instrument;
    }

    private static string CategoryParameter(NasdaqNordicCategory category) =>
        category == NasdaqNordicCategory.MainMarket ? "MAIN_MARKET" : "FIRST_NORTH";
}
