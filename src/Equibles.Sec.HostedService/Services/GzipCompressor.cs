using System.IO.Compression;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Gzip helper shared by the SEC ingest services that cache raw filing payloads
/// (the XBRL envelope on a document, the ownership XML on an insider filing).
/// </summary>
internal static class GzipCompressor
{
    public static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }
}
