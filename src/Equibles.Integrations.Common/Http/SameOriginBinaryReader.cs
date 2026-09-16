namespace Equibles.Integrations.Common.Http;

// One bounded response from a publisher's own HTTPS origin, kept as bytes. A redirect off the origin or a body
// past the cap is a signal to re-verify the source, never something to follow or truncate.
public static class SameOriginBinaryReader
{
    public static async Task<SameOriginPayload> Read(
        HttpClient httpClient,
        Uri origin,
        Uri uri,
        int maxBytes,
        CancellationToken cancellationToken,
        string accept = null
    )
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(uri);
        if (!IsOnOrigin(origin, uri))
            throw new InvalidDataException($"{uri} is not on {origin.Host}.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (accept != null)
            request.Headers.Accept.ParseAdd(accept);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } actual && !IsOnOrigin(origin, actual))
            throw new InvalidDataException($"{origin.Host} response left its official origin.");
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"{origin.Host} response exceeds the capture limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException(
                    $"{origin.Host} response exceeds the capture limit."
                );
            output.Write(buffer, 0, read);
        }
        return new SameOriginPayload(
            output.ToArray(),
            response.Content.Headers.ContentType?.CharSet
        );
    }

    public static bool IsOnOrigin(Uri origin, Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;
}

public sealed record SameOriginPayload(byte[] Bytes, string CharSet);
