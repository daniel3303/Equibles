using Equibles.Integrations.Bme.Models;
using Equibles.Integrations.Common.Http;

namespace Equibles.Integrations.Bme;

// Plain GETs against the exchange's public market API; the listed-companies reply is one unpaged list and
// each share line is confirmed by its own details reply.
public class BmeClient(HttpClient httpClient)
{
    private static readonly Uri Origin = new("https://apiweb.bolsasymercados.es");
    private const string Accept = "application/json";
    private const int MaxListBytes = 4_000_000;
    private const int MaxDetailsBytes = 1_000_000;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var json = await SameOriginTextReader.Read(
            httpClient,
            Origin,
            ListedCompaniesUrl,
            MaxListBytes,
            timeout.Token,
            Accept
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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var url = ShareDetailsUrl(isin);
        var json = await SameOriginTextReader.Read(
            httpClient,
            Origin,
            url,
            MaxDetailsBytes,
            timeout.Token,
            Accept
        );
        var details = BmeParser.ReadShareDetails(json);
        if (details.Isin != isin)
            throw new InvalidDataException("BME share details answer for another security.");
        details.SourceUrl = url;
        return details;
    }
}
