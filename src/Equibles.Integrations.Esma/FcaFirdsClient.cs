using System.Globalization;
using System.Text.Json;
using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

// The FCA mirrors the FIRDS file format for UK instruments; its index is a search endpoint without checksums.
public class FcaFirdsClient(HttpClient httpClient) : IFirdsFileIndex
{
    public const string AuthorityCode = "FCA";
    private const int PageSize = 500;
    private const int MaxIndexBytes = 8_000_000;
    private static readonly Uri IndexOrigin = new("https://api.data.fca.org.uk");
    private const string DownloadHost = "data.fca.org.uk";

    public string Authority => AuthorityCode;

    public async Task<IReadOnlyList<FirdsFile>> ListEquityFiles(
        DateOnly publishedOnOrAfter,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<FirdsFile>();
        var since = publishedOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // The index may answer fewer hits than requested; the cursor advances by what it returned.
        for (var from = 0; from < 100_000; )
        {
            var query =
                "/fca_data_firds_files?q="
                + Uri.EscapeDataString(
                    $"((file_type:FULINS AND file_name:FULINS_E*) OR file_type:DLTINS) AND publication_date:[{since} TO *]"
                )
                + $"&from={from}&size={PageSize}&sort="
                + Uri.EscapeDataString("publication_date:asc");
            using var document = JsonDocument.Parse(
                await Read(new Uri(IndexOrigin, query), cancellationToken)
            );
            var page = document.RootElement.GetProperty("hits");
            // The total arrives as a number or an ES7 {value, relation} object; without one a short page ends the walk.
            int? found = page.TryGetProperty("total", out var total)
                ? total.ValueKind == JsonValueKind.Object
                    ? total.GetProperty("value").GetInt32()
                    : total.GetInt32()
                : null;
            var hits = page.GetProperty("hits");
            foreach (var hit in hits.EnumerateArray())
            {
                var source = hit.GetProperty("_source");
                var name = source.GetProperty("file_name").GetString();
                if (!FirdsFileNames.TryParse(name, out var type, out var publishedOn))
                    continue;
                var link = source.GetProperty("download_link").GetString();
                if (!Uri.TryCreate(link, UriKind.Absolute, out var url))
                    throw new InvalidDataException("FCA index entry lacks a download link.");
                files.Add(
                    new FirdsFile
                    {
                        Authority = AuthorityCode,
                        FileName = name,
                        FileType = type,
                        PublishedOn = publishedOn,
                        DownloadUrl = url,
                    }
                );
            }
            var returned = hits.GetArrayLength();
            if (returned == 0)
                break;
            from += returned;
            if (found == null ? returned < PageSize : from >= found)
                break;
        }
        return files.OrderBy(file => file.PublishedOn).ThenBy(file => file.FileName).ToList();
    }

    public Task<FirdsDownload> Download(
        FirdsFile file,
        CancellationToken cancellationToken = default
    ) => FirdsDownloader.Download(httpClient, file, DownloadHost, cancellationToken);

    private async Task<string> Read(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxIndexBytes)
            throw new InvalidDataException("FCA index response exceeds its capture limit.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > MaxIndexBytes)
            throw new InvalidDataException("FCA index response exceeds its capture limit.");
        return body;
    }
}
