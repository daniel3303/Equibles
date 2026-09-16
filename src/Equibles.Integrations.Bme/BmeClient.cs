using Equibles.Integrations.Bme.Models;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.Integrations.Bme;

// Plain GETs against the exchange's public market API; the listed-companies reply is one unpaged list and
// each share line is confirmed by its own details reply.
public class BmeClient(HttpClient httpClient)
{
    private static readonly Uri Origin = new("https://apiweb.bolsasymercados.es");
    private const string Accept = "application/json";
    private const int MaxListBytes = 4_000_000;
    private const int MaxDetailsBytes = 1_000_000;

    // A capture asks for one instrument per listed company, and the venue refused the twenty-sixth reply of
    // a first pass that had been asking about fifteen times a second. One request a second is the pace.
    internal const int MinimumRequestIntervalSeconds = 1;

    // The typed client is transient, so the pace is shared statically, as GLEIF's is. The interval is a
    // constant, so no ordering between static fields can leave this limiter pacing nothing.
    private static readonly IRateLimiter VenuePace = new RateLimiter(
        1,
        TimeSpan.FromSeconds(MinimumRequestIntervalSeconds)
    );

    // Every request the client makes waits its turn here; a caller may hold it to a pace of its own.
    internal IRateLimiter Pace { get; init; } = VenuePace;

    public static readonly Uri ListedCompaniesUrl = new(
        Origin,
        "/Market/v1/EQ/ListedCompanies?ISIN=&sectorKey=&subsectorKey=&tradingSystem=SIBE&mtfSegment=&page=0&pageSize=0"
    );

    public static Uri ShareDetailsUrl(string isin) =>
        new(Origin, $"/Market/v1/EQ/ShareDetailsInfo?ISIN={isin}");

    public async Task<BmeListedCompanyList> GetListedCompanies(
        CancellationToken cancellationToken = default
    )
    {
        var json = await Read(
            ListedCompaniesUrl,
            MaxListBytes,
            TimeSpan.FromMinutes(2),
            cancellationToken
        );
        var list = BmeParser.ReadListedCompanies(json);
        list.SourceUrl = ListedCompaniesUrl;
        list.CapturedAt = DateTime.UtcNow;
        return list;
    }

    public async Task<BmeShareDetails> GetShareDetails(
        string isin,
        CancellationToken cancellationToken = default
    )
    {
        if (!Core.Identity.InternationalSecurityIdentifiers.IsValidIsin(isin))
            throw new InvalidDataException("BME share details need a valid ISIN.");
        var url = ShareDetailsUrl(isin);
        var json = await Read(url, MaxDetailsBytes, TimeSpan.FromSeconds(30), cancellationToken);
        var details = BmeParser.ReadShareDetails(json);
        if (details.Isin != isin)
            throw new InvalidDataException("BME share details answer for another security.");
        details.SourceUrl = url;
        return details;
    }

    // The wait runs on the caller's token, so time spent queueing is not charged against the reply's budget.
    private async Task<string> Read(
        Uri url,
        int maxBytes,
        TimeSpan budget,
        CancellationToken cancellationToken
    )
    {
        await Pace.WaitAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        return await SameOriginTextReader.Read(
            httpClient,
            Origin,
            url,
            maxBytes,
            timeout.Token,
            Accept
        );
    }
}
