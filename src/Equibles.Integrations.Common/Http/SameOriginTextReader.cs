using System.Text;

namespace Equibles.Integrations.Common.Http;

// Reads one bounded text response from a publisher's own HTTPS origin; a redirect elsewhere or an oversized
// body is a signal to re-verify the source, never something to follow or truncate.
public static class SameOriginTextReader
{
    public static async Task<string> Read(
        HttpClient httpClient,
        Uri origin,
        Uri uri,
        int maxBytes,
        CancellationToken cancellationToken,
        string accept = null
    )
    {
        var payload = await SameOriginBinaryReader.Read(
            httpClient,
            origin,
            uri,
            maxBytes,
            cancellationToken,
            accept
        );
        return Decode(payload.CharSet, payload.Bytes);
    }

    // The declared charset is honoured; an unknown or missing one reads as UTF-8.
    public static string Decode(string charSet, byte[] bytes)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charSet))
            try
            {
                encoding = Encoding.GetEncoding(charSet.Trim('"'));
            }
            catch (ArgumentException) { }
        return encoding.GetString(bytes);
    }
}
