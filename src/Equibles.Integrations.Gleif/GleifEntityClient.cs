using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Equibles.Core.Identity;
using Equibles.Integrations.Gleif.Models;
using Microsoft.Extensions.Logging;

namespace Equibles.Integrations.Gleif;

// Name search and parent links for organizations a document names. A search hit only ranks a candidate for
// the caller's own verified decision; a legal name is never an identity by itself.
public partial class GleifEntityClient
{
    public const int MaxSearchResults = 20;
    private const int MaxQueryLength = 200;

    private readonly HttpClient _httpClient;
    private readonly ILogger<GleifEntityClient> _logger;

    // One constructor only: typed-client activation refuses a class whose constructors are ambiguous.
    public GleifEntityClient(HttpClient httpClient, ILogger<GleifEntityClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // Full-text search over legal, other and transliterated names. Commas go first: GLEIF reads one inside a
    // filter value as a list separator, so "Co., Ltd." returned every entity named "Ltd".
    public virtual async Task<GleifEntitySearch> Search(
        string name,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    )
    {
        var query = Spaces().Replace((name ?? "").Replace(',', ' '), " ").Trim();
        if (query.Length == 0 || query.Length > MaxQueryLength)
            throw new ArgumentException("A name of 1 to 200 characters is required.", nameof(name));
        if (maxResults is < 1 or > MaxSearchResults)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        var source = new Uri(
            GleifHttp.Origin,
            "/api/v1/lei-records?filter%5Bfulltext%5D="
                + Uri.EscapeDataString(query)
                + "&page%5Bsize%5D="
                + maxResults
        );
        var body = await GleifHttp.Read(_httpClient, source, _logger, cancellationToken);
        return Parse(
            body,
            "GLEIF entity search response has an invalid structure.",
            root =>
            {
                var total = root.GetProperty("meta").GetProperty("pagination").GetProperty("total");
                if (!total.TryGetInt32(out var count) || count < 0)
                    throw new InvalidDataException("GLEIF total is invalid.");
                var data = root.GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("GLEIF data must be an array.");
                var result = new GleifEntitySearch
                {
                    Query = query,
                    SourceUrl = source,
                    Total = count,
                };
                foreach (var record in data.EnumerateArray().Take(maxResults))
                    result.Entities.Add(Entity(record, null));
                return result;
            }
        );
    }

    public virtual async Task<GleifEntity> GetEntity(
        string lei,
        CancellationToken cancellationToken = default
    )
    {
        RequireLei(lei);
        var source = new Uri(GleifHttp.Origin, "/api/v1/lei-records/" + lei);
        var body = await GleifHttp.Read(_httpClient, source, _logger, cancellationToken);
        var entity = Parse(
            body,
            "GLEIF legal entity response has an invalid structure.",
            root => Entity(root.GetProperty("data"), source)
        );
        if (entity.LegalEntityIdentifier != lei)
            throw new InvalidDataException("GLEIF did not confirm the requested LEI.");
        return entity;
    }

    // The entity's direct accounting parent, or null when GLEIF records none.
    public virtual Task<GleifEntity> GetDirectParent(
        string lei,
        CancellationToken cancellationToken = default
    ) => GetParent(lei, "direct-parent", cancellationToken);

    // The top of the entity's consolidation chain, or null when GLEIF records none (Alphabet has no parent).
    public virtual Task<GleifEntity> GetUltimateParent(
        string lei,
        CancellationToken cancellationToken = default
    ) => GetParent(lei, "ultimate-parent", cancellationToken);

    private async Task<GleifEntity> GetParent(
        string lei,
        string relationship,
        CancellationToken cancellationToken
    )
    {
        RequireLei(lei);
        var source = new Uri(GleifHttp.Origin, $"/api/v1/lei-records/{lei}/{relationship}");
        string body;
        try
        {
            body = await GleifHttp.Read(_httpClient, source, _logger, cancellationToken);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var parent = Parse(
            body,
            "GLEIF parent response has an invalid structure.",
            root => Entity(root.GetProperty("data"), source)
        );
        if (parent.LegalEntityIdentifier == lei)
            throw new InvalidDataException("GLEIF named an entity as its own parent.");
        return parent;
    }

    private static T Parse<T>(string body, string invalid, Func<JsonElement, T> read)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return read(json.RootElement);
        }
        catch (Exception exception)
            when (exception is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            throw new InvalidDataException(invalid, exception);
        }
    }

    private static GleifEntity Entity(JsonElement record, Uri source)
    {
        var attributes = record.GetProperty("attributes");
        var lei = Text(attributes, "lei");
        if (
            !InternationalSecurityIdentifiers.IsValidLei(lei)
            || Text(record, "id") != lei
            || Text(record, "type") != "lei-records"
        )
            throw new InvalidDataException("GLEIF entity identifiers disagree.");
        var entity = attributes.GetProperty("entity");
        var legalName = entity.GetProperty("legalName");
        var result = new GleifEntity
        {
            LegalEntityIdentifier = lei,
            LegalName = Text(legalName, "name"),
            LegalNameLanguage = OptionalText(legalName, "language"),
            Country = Text(entity.GetProperty("legalAddress"), "country"),
            Jurisdiction = OptionalText(entity, "jurisdiction"),
            Category = OptionalText(entity, "category"),
            EntityStatus = Text(entity, "status"),
            RegistrationStatus = Text(attributes.GetProperty("registration"), "status"),
            SourceUrl = source ?? new Uri(GleifHttp.Origin, "/api/v1/lei-records/" + lei),
        };
        AddNames(result.OtherNames, entity, "otherNames", false);
        AddNames(result.OtherNames, entity, "transliteratedOtherNames", true);
        return result;
    }

    private static void AddNames(
        List<GleifEntityName> names,
        JsonElement entity,
        string property,
        bool transliterated
    )
    {
        if (
            !entity.TryGetProperty(property, out var values)
            || values.ValueKind != JsonValueKind.Array
        )
            return;
        foreach (var value in values.EnumerateArray())
        {
            names.Add(
                new GleifEntityName
                {
                    Name = Text(value, "name"),
                    Language = OptionalText(value, "language"),
                    Type = OptionalText(value, "type"),
                    IsTransliterated = transliterated,
                }
            );
        }
    }

    private static void RequireLei(string lei)
    {
        if (!InternationalSecurityIdentifiers.IsValidLei(lei))
            throw new ArgumentException("A complete LEI is required.", nameof(lei));
    }

    private static string Text(JsonElement element, string name) =>
        OptionalText(element, name)
        ?? throw new InvalidDataException($"GLEIF entity lacks {name}.");

    private static string OptionalText(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
