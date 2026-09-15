using System.Text;
using Equibles.Integrations.Xetra.Models;

namespace Equibles.Integrations.Xetra;

public class XetraInstrumentListClient(HttpClient httpClient)
{
    private static readonly Uri ListingPage = new(
        "https://www.cashmarket.deutsche-boerse.com/cash-en/trading/Tradable-Instruments-Xetra"
    );
    private const int MaxPageBytes = 4_000_000;
    private const int MaxCsvBytes = 32_000_000;

    public async Task<XetraInstrumentList> GetInstruments(
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var html = await Read(ListingPage, MaxPageBytes, timeout.Token);
        var source = new Uri(ListingPage, XetraInstrumentListParser.ReadDownloadPath(html));
        var csv = await Read(source, MaxCsvBytes, timeout.Token);
        var list = XetraInstrumentListParser.Read(csv);
        list.PageUrl = ListingPage;
        list.SourceUrl = source;
        list.CapturedAt = DateTime.UtcNow;
        return list;
    }

    private async Task<string> Read(Uri uri, int maxBytes, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        // The publisher has moved this page across hosts before; a redirect is a signal to re-verify, not to follow.
        if (
            response.RequestMessage?.RequestUri is { } actual
            && (
                actual.Scheme != "https"
                || actual.Host != ListingPage.Host
                || !actual.IsDefaultPort
            )
        )
            throw new InvalidDataException("Xetra response left its official HTTPS origin.");
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException("Xetra response exceeds the capture limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException("Xetra response exceeds the capture limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }
}
