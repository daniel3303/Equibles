using System.IO.Compression;

namespace Equibles.Media.BusinessLogic;

/// <summary>
/// Gzip helper for the internal blobs the application caches (the XBRL envelope
/// on a document, the ownership XML on an insider filing). Shared so capture and
/// replay use the same compression.
/// </summary>
public static class GzipCompressor
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

    public static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
