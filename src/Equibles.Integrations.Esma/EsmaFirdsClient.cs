using System.Globalization;
using System.Text.Json;
using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

// ESMA publishes its file index through a public Solr core; the register itself is the zip host.
public class EsmaFirdsClient(HttpClient httpClient) : IFirdsFileIndex
{
    public const string AuthorityCode = "ESMA";
    private const int PageSize = 500;
    private const int MaxIndexBytes = 8_000_000;
    private static readonly Uri IndexOrigin = new("https://registers.esma.europa.eu");
    private const string DownloadHost = "firds.esma.europa.eu";

    public string Authority => AuthorityCode;

    public async Task<IReadOnlyList<FirdsFile>> ListEquityFiles(
        DateOnly publishedOnOrAfter,
        CancellationToken cancellationToken = default
    )
    {
        var files = new List<FirdsFile>();
        var since = publishedOnOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // The index may answer fewer rows than requested; the cursor advances by what it returned.
        for (var start = 0; start < 100_000; )
        {
            var query =
                "/solr/esma_registers_firds_files/select?q=*"
                + "&fq="
                + Uri.EscapeDataString($"publication_date:[{since}T00:00:00Z TO *]")
                + "&fq="
                + Uri.EscapeDataString("file_type:(FULINS OR DLTINS)")
                + "&wt=json&sort="
                + Uri.EscapeDataString("publication_date asc,file_name asc")
                + $"&rows={PageSize}&start={start}";
            using var document = JsonDocument.Parse(
                await Read(new Uri(IndexOrigin, query), cancellationToken)
            );
            var response = document.RootElement.GetProperty("response");
            var found = response.GetProperty("numFound").GetInt32();
            var docs = response.GetProperty("docs");
            foreach (var entry in docs.EnumerateArray())
            {
                var name = entry.GetProperty("file_name").GetString();
                if (!FirdsFileNames.TryParse(name, out var type, out var publishedOn))
                    continue;
                var link = entry.GetProperty("download_link").GetString();
                if (!Uri.TryCreate(link, UriKind.Absolute, out var url))
                    throw new InvalidDataException("ESMA index entry lacks a download link.");
                files.Add(
                    new FirdsFile
                    {
                        Authority = AuthorityCode,
                        FileName = name,
                        FileType = type,
                        PublishedOn = publishedOn,
                        DownloadUrl = url,
                        Checksum = entry.TryGetProperty("checksum", out var checksum)
                            ? checksum.GetString()
                            : null,
                    }
                );
            }
            var returned = docs.GetArrayLength();
            if (returned == 0)
                break;
            start += returned;
            if (start >= found)
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
            throw new InvalidDataException("ESMA index response exceeds its capture limit.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > MaxIndexBytes)
            throw new InvalidDataException("ESMA index response exceeds its capture limit.");
        return body;
    }
}
