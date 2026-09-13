using System.Text;
using System.Text.Json;
using Equibles.Core.Identity;
using Equibles.Integrations.Gleif.Models;

namespace Equibles.Integrations.Gleif;

// Exact identifier relationships only; legal names are descriptive, never matching keys.
public class GleifIdentityClient(HttpClient httpClient)
{
    private const int MaxResponseBytes = 2_000_000;
    private static readonly Uri Origin = new("https://api.gleif.org");

    public async Task<GleifIssuerIdentity> GetIssuerForIsin(
        string isin,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await ReadIssuerForIsin(isin, cancellationToken);
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            throw new InvalidDataException(
                "GLEIF identity response has an invalid structure.",
                exception
            );
        }
    }

    private async Task<GleifIssuerIdentity> ReadIssuerForIsin(
        string isin,
        CancellationToken cancellationToken
    )
    {
        if (!InternationalSecurityIdentifiers.IsValidIsin(isin))
            throw new ArgumentException("A complete ISIN is required.", nameof(isin));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var source = new Uri(
            Origin,
            "/api/v1/lei-records?filter%5Bisin%5D=" + Uri.EscapeDataString(isin)
        );
        var result = new GleifIssuerIdentity { RequestedIsin = isin, SourceUrl = source };
        var body = await Read(source, timeout.Token);
        result.ResponseBodies.Add(body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var total = Total(root);
        var records = Records(root);
        if (total == 0 && records.GetArrayLength() == 0)
            return result;
        if (total != 1 || records.GetArrayLength() != 1)
            throw new InvalidDataException("GLEIF must identify exactly one issuer for this ISIN.");
        var record = records[0];
        var attributes = record.GetProperty("attributes");
        var lei = Text(attributes, "lei");
        if (
            !InternationalSecurityIdentifiers.IsValidLei(lei)
            || Text(record, "id") != lei
            || Text(record, "type") != "lei-records"
        )
            throw new InvalidDataException("GLEIF issuer identifiers disagree.");
        result.LegalEntityIdentifier = lei;
        var entity = attributes.GetProperty("entity");
        result.LegalName = Text(entity.GetProperty("legalName"), "name");
        result.Jurisdiction = Text(entity, "jurisdiction");
        result.EntityStatus = Text(entity, "status");
        result.RegistrationStatus = Text(attributes.GetProperty("registration"), "status");
        var related = record.GetProperty("relationships").GetProperty("isins").GetProperty("links");
        var path = $"/api/v1/lei-records/{lei}/isins";
        var next = ValidateRelatedUrl(Text(related, "related"), path);
        var publishDate = Text(root.GetProperty("meta").GetProperty("goldenCopy"), "publishDate");
        await ReadRelatedIsins(result, next, path, publishDate, timeout.Token);
        if (!result.RelatedIsins.Contains(isin, StringComparer.Ordinal))
            throw new InvalidDataException(
                "GLEIF related securities do not confirm the requested ISIN."
            );
        return result;
    }

    private async Task ReadRelatedIsins(
        GleifIssuerIdentity result,
        Uri next,
        string path,
        string publishDate,
        CancellationToken token
    )
    {
        var visited = new HashSet<string>();
        var seen = new HashSet<string>();
        int? expected = null;
        var bytes = Encoding.UTF8.GetByteCount(result.ResponseBodies[0]);
        for (var page = 0; next != null && page < 100; page++)
        {
            if (!visited.Add(next.AbsoluteUri))
                throw new InvalidDataException("GLEIF pagination repeated a page.");
            var body = await Read(next, token);
            bytes += Encoding.UTF8.GetByteCount(body);
            if (bytes > 10_000_000)
                throw new InvalidDataException(
                    "GLEIF identity capture exceeds its aggregate limit."
                );
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var total = Total(root);
            expected ??= total;
            if (
                total != expected
                || total > 10_000
                || Text(root.GetProperty("meta").GetProperty("goldenCopy"), "publishDate")
                    != publishDate
            )
                throw new InvalidDataException("GLEIF identity changed during pagination.");
            var rows = Records(root);
            if (rows.GetArrayLength() == 0)
                throw new InvalidDataException("GLEIF returned an incomplete securities page.");
            foreach (var row in rows.EnumerateArray())
            {
                var attributes = row.GetProperty("attributes");
                var isin = Text(attributes, "isin");
                if (
                    Text(row, "type") != "isins"
                    || Text(attributes, "lei") != result.LegalEntityIdentifier
                    || !InternationalSecurityIdentifiers.IsValidIsin(isin)
                    || !seen.Add(isin)
                )
                    throw new InvalidDataException(
                        "GLEIF related security identity is invalid, conflicting, or repeated."
                    );
                result.RelatedIsins.Add(isin);
            }
            result.ResponseBodies.Add(body);
            var links = root.GetProperty("links");
            next =
                links.TryGetProperty("next", out var value) && value.ValueKind != JsonValueKind.Null
                    ? ValidateRelatedUrl(value.GetString(), path)
                    : null;
            if (
                result.RelatedIsins.Count > expected
                || (next == null && result.RelatedIsins.Count != expected)
            )
                throw new InvalidDataException(
                    "GLEIF related securities do not match the reported total."
                );
        }
        if (next != null)
            throw new InvalidDataException("GLEIF pagination exceeded its page limit.");
    }

    private static Uri ValidateRelatedUrl(string value, string path)
    {
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || uri.Host != Origin.Host
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.AbsolutePath != path
            || uri.Fragment.Length != 0
        )
            throw new InvalidDataException(
                "GLEIF related URL does not identify the requested issuer's securities."
            );
        var keys = new HashSet<string>();
        foreach (
            var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            if (
                pair.Length != 2
                || key is not ("page[number]" or "page[size]")
                || !keys.Add(key)
                || !int.TryParse(pair[1], out var number)
                || number <= 0
                || number > 10_000
            )
                throw new InvalidDataException(
                    "GLEIF related pagination contains unsupported filters."
                );
        }
        return uri;
    }

    private async Task<string> Read(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            token
        );
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } actual && actual != uri)
            throw new InvalidDataException("GLEIF identity response was redirected.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new InvalidDataException("GLEIF identity response exceeds its capture limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[16_384];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidDataException(
                    "GLEIF identity response exceeds its capture limit."
                );
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static string Text(JsonElement element, string name)
    {
        if (
            !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString())
        )
            throw new InvalidDataException($"GLEIF identity lacks {name}.");
        return value.GetString();
    }

    private static int Total(JsonElement root)
    {
        var value = root.GetProperty("meta").GetProperty("pagination").GetProperty("total");
        if (!value.TryGetInt32(out var total) || total < 0)
            throw new InvalidDataException("GLEIF total is invalid.");
        return total;
    }

    private static JsonElement Records(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GLEIF data must be an array.");
        return data;
    }
}
