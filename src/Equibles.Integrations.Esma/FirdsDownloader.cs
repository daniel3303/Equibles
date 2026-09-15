using System.Security.Cryptography;
using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

// Shared download path: same-origin only, byte-capped to disk, checksum verified when the index states one.
public static class FirdsDownloader
{
    private const long MaxZipBytes = 200_000_000;
    private const string FilePrefix = "firds-";

    // A crash between creating a temp file and disposing it leaves up to 200 MB behind; the next run reclaims it.
    public static void SweepStaleFiles(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        IEnumerable<string> stale;
        try
        {
            stale = Directory
                .EnumerateFiles(Path.GetTempPath(), FilePrefix + "*.zip")
                .Where(path => File.GetLastWriteTimeUtc(path) < cutoff)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var path in stale)
            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException) { }
    }

    public static async Task<FirdsDownload> Download(
        HttpClient httpClient,
        FirdsFile file,
        string expectedHost,
        CancellationToken cancellationToken
    )
    {
        if (
            file.DownloadUrl is not { IsAbsoluteUri: true, Scheme: "https" }
            || file.DownloadUrl.Host != expectedHost
            || !file.DownloadUrl.IsDefaultPort
        )
            throw new InvalidDataException("FIRDS download must stay on the authority's origin.");
        using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } actual && actual != file.DownloadUrl)
            throw new InvalidDataException("FIRDS download was redirected.");
        if (response.Content.Headers.ContentLength > MaxZipBytes)
            throw new InvalidDataException("FIRDS file exceeds its download limit.");
        var path = Path.Combine(
            Path.GetTempPath(),
            FilePrefix + Guid.NewGuid().ToString("N") + ".zip"
        );
        try
        {
            long total = 0;
            using var md5 = MD5.Create();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[81_920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxZipBytes)
                        throw new InvalidDataException("FIRDS file exceeds its download limit.");
                    md5.TransformBlock(buffer, 0, read, null, 0);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            md5.TransformFinalBlock([], 0, 0);
            var digest = Convert.ToHexStringLower(md5.Hash);
            if (
                file.Checksum != null
                && !string.Equals(digest, file.Checksum, StringComparison.OrdinalIgnoreCase)
            )
                throw new InvalidDataException(
                    "FIRDS file checksum does not match its index entry."
                );
            return new FirdsDownload(file, path, total);
        }
        catch
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { }
            throw;
        }
    }
}
