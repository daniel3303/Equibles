using System.Net;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.Lse.Models;

namespace Equibles.Integrations.Lse;

// The exchange publishes its instrument list as a numbered workbook and its own reports page is a script
// application, so the newest edition is found by asking the document host which numbers exist.
public class LseInstrumentListClient(HttpClient httpClient)
{
    public static readonly Uri PublisherPage = new(
        "https://www.londonstockexchange.com/reports?tab=instruments"
    );

    internal static readonly Uri Origin = new("https://docs.londonstockexchange.com");

    // The edition published when this build was written; editions only ever count up.
    internal const int FirstEdition = 81;
    internal const int MaxEditionsAhead = 36;
    internal const int MissesBeforeStop = 4;
    private const int MaxWorkbookBytes = 64_000_000;
    private const long MaxPartBytes = 64_000_000;

    public async Task<LseInstrumentList> GetInstruments(
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var edition = await FindLatestEdition(timeout.Token);
        var source = EditionUrl(edition);
        var payload = await SameOriginBinaryReader.Read(
            httpClient,
            Origin,
            source,
            MaxWorkbookBytes,
            timeout.Token
        );
        var list = LseInstrumentListParser.Read(payload.Bytes, MaxPartBytes);
        list.PageUrl = PublisherPage;
        list.SourceUrl = source;
        list.Edition = edition;
        list.CapturedAt = DateTime.UtcNow;
        return list;
    }

    internal static Uri EditionUrl(int edition) =>
        new($"{Origin.AbsoluteUri}sites/default/files/reports/Instrument%20list_{edition}.xlsx");

    // Editions are not published monthly and the series has gaps, so the walk carries on over a few missing
    // numbers and keeps the highest that answers; finding none is a failure, never an empty market.
    internal async Task<int> FindLatestEdition(CancellationToken cancellationToken)
    {
        var latest = 0;
        var misses = 0;
        for (
            var edition = FirstEdition;
            edition < FirstEdition + MaxEditionsAhead && misses < MissesBeforeStop;
            edition++
        )
            if (await Exists(EditionUrl(edition), cancellationToken))
            {
                latest = edition;
                misses = 0;
            }
            else
                misses++;
        return latest > 0
            ? latest
            : throw new InvalidDataException(
                "The London instrument list is no longer published where this build looks for it."
            );
    }

    private async Task<bool> Exists(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            return false;
        response.EnsureSuccessStatusCode();
        if (
            response.RequestMessage?.RequestUri is { } actual
            && !SameOriginBinaryReader.IsOnOrigin(Origin, actual)
        )
            throw new InvalidDataException($"{Origin.Host} response left its official origin.");
        // The host answers a missing edition with an HTML page under some conditions; only a document is an edition.
        return response.Content.Headers.ContentType?.MediaType?.StartsWith(
                "application/",
                StringComparison.OrdinalIgnoreCase
            ) == true;
    }
}
