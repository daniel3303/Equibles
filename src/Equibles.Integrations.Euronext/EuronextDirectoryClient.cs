using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Integrations.Euronext.Models;

namespace Equibles.Integrations.Euronext;

public class EuronextDirectoryClient(HttpClient httpClient)
{
    private static readonly Uri LisbonDirectory = new(
        "https://live.euronext.com/en/markets/lisbon/equities/list"
    );
    private const int PageSize = 100;
    private const int MaxResponseBytes = 2_000_000;
    private const int MaxSnapshotBytes = 10_000_000;

    public async Task<EuronextDirectorySnapshot> GetLisbonEquities(
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        using var directoryRequest = new HttpRequestMessage(HttpMethod.Get, LisbonDirectory);
        var directoryHtml = await Read(directoryRequest, token);
        var gateway = EuronextDirectoryParser.ReadLisbonGateway(directoryHtml);
        var snapshot = new EuronextDirectorySnapshot
        {
            SourceUrl = LisbonDirectory,
            DirectoryHtml = directoryHtml,
        };
        var identities = new HashSet<(string Isin, string Mic)>();
        var symbols = new HashSet<(string Mic, string Symbol)>();
        var capturedBytes = Encoding.UTF8.GetByteCount(directoryHtml);
        int? expectedTotal = null;
        for (var pageNumber = 0; pageNumber < 100; pageNumber++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, gateway)
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        ["iDisplayStart"] = snapshot.Listings.Count.ToString(
                            CultureInfo.InvariantCulture
                        ),
                        ["iDisplayLength"] = PageSize.ToString(CultureInfo.InvariantCulture),
                        ["sSortDir_0"] = "asc",
                        ["sSortField"] = "name",
                        ["args[display_datapoints]"] = EuronextDirectoryParser.DataPoints,
                    }
                ),
            };
            var body = await Read(request, token);
            capturedBytes += Encoding.UTF8.GetByteCount(body);
            if (capturedBytes > MaxSnapshotBytes)
                throw new InvalidDataException(
                    "Euronext snapshot exceeds its aggregate capture limit."
                );
            var page = EuronextDirectoryParser.ReadLisbonPage(body);
            expectedTotal ??= page.TotalRecords;
            if (page.TotalRecords != expectedTotal || page.Listings.Count > PageSize)
                throw new InvalidDataException(
                    "Euronext directory changed size during pagination."
                );
            foreach (var listing in page.Listings)
            {
                if (!identities.Add((listing.Isin, listing.MarketIdentifierCode)))
                    throw new InvalidDataException(
                        "Euronext pagination repeated a listing; refusing an incomplete snapshot."
                    );
                if (!symbols.Add((listing.MarketIdentifierCode, listing.Symbol)))
                    throw new InvalidDataException(
                        "Euronext directory gives one market symbol multiple security identities."
                    );
                snapshot.Listings.Add(listing);
            }
            snapshot.ResponseBodies.Add(body);
            if (snapshot.Listings.Count == expectedTotal)
            {
                snapshot.CapturedAt = DateTime.UtcNow;
                return snapshot;
            }
            if (snapshot.Listings.Count > expectedTotal)
                throw new InvalidDataException("Euronext directory exceeded its reported total.");
        }
        throw new InvalidDataException(
            "Euronext directory pagination did not complete within its bound."
        );
    }

    public async Task<EuronextInstrumentIdentity> GetInstrumentIdentity(
        EuronextEquityListing listing,
        CancellationToken cancellationToken = default
    )
    {
        var source = EuronextDirectoryParser.ValidateProductUrl(listing);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        var html = await Read(request, timeout.Token);
        return EuronextDirectoryParser.ReadInstrumentIdentity(listing, html);
    }

    private async Task<string> Read(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        // A redirected external source must never be accepted as Euronext listing evidence.
        if (
            response.RequestMessage?.RequestUri is { } actual
            && (
                actual.Scheme != "https"
                || actual.Host != "live.euronext.com"
                || !actual.IsDefaultPort
            )
        )
            throw new InvalidDataException("Euronext response left its official HTTPS origin.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("Euronext response exceeds the capture limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException("Euronext response exceeds the capture limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
