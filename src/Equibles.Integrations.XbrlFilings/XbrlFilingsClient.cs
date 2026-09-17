using Equibles.Integrations.Common.Http;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.XbrlFilings.Models;

namespace Equibles.Integrations.XbrlFilings;

// Plain unauthenticated GETs against the filing index and the reports it addresses. The host is a nonprofit
// serving the whole European corpus for free, so every call waits a full second behind the one before it.
public class XbrlFilingsClient(HttpClient httpClient)
{
    private static readonly Uri Origin = new("https://filings.xbrl.org");
    private const string IndexAccept = "application/vnd.api+json";
    private const int MaxIndexBytes = 4_000_000;

    // The largest report sampled across twelve markets was 125 MB, and the extractor has handled a 201 MB
    // envelope in production, so the cap refuses an outlier rather than bounding the ordinary case.
    public const int MaxReportBytes = 160_000_000;

    // The host asks for no key and states no quota, which is exactly why the pace is stated here instead.
    internal const int MinimumRequestIntervalSeconds = 1;

    // The typed client is transient, so the pace is shared statically, as GLEIF's and BME's are. The interval
    // is a constant, so no ordering between static fields can leave this limiter pacing nothing.
    private static readonly IRateLimiter HostPace = new RateLimiter(
        1,
        TimeSpan.FromSeconds(MinimumRequestIntervalSeconds)
    );

    // Every request the client makes waits its turn here; a caller may hold it to a pace of its own.
    internal IRateLimiter Pace { get; init; } = HostPace;

    public static Uri IndexUrl(string countryCode, int pageNumber, int pageSize) =>
        new(
            Origin,
            $"/api/filings?include=entity&filter%5Bcountry%5D={Uri.EscapeDataString(countryCode)}"
                + $"&page%5Bsize%5D={pageSize}&page%5Bnumber%5D={pageNumber}"
        );

    // The whole corpus, unfiltered. A filing's country is where the report was FILED, which need not be the
    // country of the market the issuer is listed on, so filtering by market country would miss an issuer that
    // files elsewhere. The index states its own total, so the caller knows where to stop.
    public static Uri IndexUrl(int pageNumber, int pageSize) =>
        new(
            Origin,
            $"/api/filings?include=entity&page%5Bsize%5D={pageSize}&page%5Bnumber%5D={pageNumber}"
        );

    public async Task<XbrlFilingPage> GetFilings(
        string countryCode,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(countryCode))
            throw new ArgumentException("A country is required.", nameof(countryCode));
        var json = await ReadText(
            IndexUrl(countryCode, pageNumber, pageSize),
            MaxIndexBytes,
            IndexAccept,
            cancellationToken
        );
        return XbrlFilingsParser.Read(json, Origin);
    }

    public async Task<XbrlFilingPage> GetFilings(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default
    )
    {
        var json = await ReadText(
            IndexUrl(pageNumber, pageSize),
            MaxIndexBytes,
            IndexAccept,
            cancellationToken
        );
        return XbrlFilingsParser.Read(json, Origin);
    }

    // The report is read at the address the index stated, up to the caller's own ceiling. A body past the
    // cap throws rather than truncating, because a half-read report parses into a plausible but short set
    // of facts. This host states no content length, so the ceiling is only reached after that many bytes of
    // the body have been read; a caller that refuses reports on it must remember the refusal, not repeat it.
    public async Task<SameOriginPayload> GetReport(
        Uri reportUrl,
        int maxBytes = MaxReportBytes,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(reportUrl);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        await Pace.WaitAsync(cancellationToken);
        return await SameOriginBinaryReader.Read(
            httpClient,
            Origin,
            reportUrl,
            Math.Min(maxBytes, MaxReportBytes),
            cancellationToken
        );
    }

    private async Task<string> ReadText(
        Uri uri,
        int maxBytes,
        string accept,
        CancellationToken cancellationToken
    )
    {
        await Pace.WaitAsync(cancellationToken);
        return await SameOriginTextReader.Read(
            httpClient,
            Origin,
            uri,
            maxBytes,
            cancellationToken,
            accept
        );
    }
}
