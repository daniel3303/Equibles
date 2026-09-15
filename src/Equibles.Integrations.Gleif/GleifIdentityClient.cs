using System.Text;
using System.Text.Json;
using Equibles.Core.Identity;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Equibles.Integrations.Gleif.Models;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Gleif;

// Exact identifier relationships only; legal names are descriptive, never matching keys.
public class GleifIdentityClient
{
    private const int MaxResponseBytes = 2_000_000;
    private const int MaxRetries = 3;
    private const int MaxRelatedPages = 100;

    // GLEIF throttled an unpaced sequential pass from about 240 requests a minute; the typed client is
    // transient, so the pace is shared statically, as FRED's is.
    internal const int RequestsPerMinute = 60;

    // The related-securities page size GLEIF honours; its default of 15 cost most issuers several calls.
    internal const int RelatedPageSize = 200;

    // Above this total an issuer (BNP Paribas lists 42,020 ISINs, mostly notes) is not enumerated: only the
    // requested ISIN, which the lookup itself confirmed, and the reported total are recorded.
    internal const int MaxRelatedIsins = 2_000;

    private static readonly Uri Origin = new("https://api.gleif.org");
    private static readonly IRateLimiter Pace = new Common.RateLimiter.RateLimiter(
        RequestsPerMinute,
        TimeSpan.FromMinutes(1)
    );

    private readonly HttpClient _httpClient;
    private readonly ILogger<GleifIdentityClient> _logger;

    // One constructor only: typed-client activation refuses a class whose constructors are ambiguous.
    public GleifIdentityClient(HttpClient httpClient, ILogger<GleifIdentityClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public virtual async Task<GleifIssuerIdentity> GetIssuerForIsin(
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
        var next = WithPageSize(ValidateRelatedUrl(Text(related, "related"), path));
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
        for (var page = 0; next != null && page < MaxRelatedPages; page++)
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
                || Text(root.GetProperty("meta").GetProperty("goldenCopy"), "publishDate")
                    != publishDate
            )
                throw new InvalidDataException("GLEIF identity changed during pagination.");
            var rows = Records(root);
            if (rows.GetArrayLength() == 0)
                throw new InvalidDataException("GLEIF returned an incomplete securities page.");
            var isins = new List<string>();
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
                isins.Add(isin);
            }
            result.ResponseBodies.Add(body);
            if (page == 0)
            {
                result.RelatedIsinCount = total;
                if (total > MaxRelatedIsins)
                {
                    _logger.LogInformation(
                        "GLEIF issuer {Lei} lists {Total} ISINs, above the {Bound} enumeration bound; only {Isin} is recorded as related",
                        result.LegalEntityIdentifier,
                        total,
                        MaxRelatedIsins,
                        result.RequestedIsin
                    );
                    result.RelatedIsins.Add(result.RequestedIsin);
                    return;
                }
            }
            result.RelatedIsins.AddRange(isins);
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

    // The first page is requested at our page size when the source's link states none; later pages follow
    // the served links as they are.
    private static Uri WithPageSize(Uri uri)
    {
        var stated = uri
            .Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(part => Uri.UnescapeDataString(part.Split('=', 2)[0]) == "page[size]");
        if (stated)
            return uri;
        var separator = uri.Query.Length == 0 ? "?" : "&";
        return new Uri(uri.AbsoluteUri + separator + "page%5Bsize%5D=" + RelatedPageSize);
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
        using var response = await HttpRetry.Send(
            () =>
                _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, uri),
                    HttpCompletionOption.ResponseHeadersRead,
                    token
                ),
            Pace,
            MaxRetries,
            "GLEIF request retries exhausted.",
            (attempt, delay) =>
                _logger.LogWarning(
                    "GLEIF rate limited (429), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            (statusCode, attempt, delay) =>
                _logger.LogWarning(
                    "GLEIF server error ({StatusCode}), retrying in {Delay}s (attempt {Attempt}/{Max})",
                    statusCode,
                    delay.TotalSeconds,
                    attempt + 1,
                    MaxRetries
                ),
            token
        );
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
