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

    // Full-text search over legal, other and transliterated names.
    public virtual async Task<GleifEntitySearch> Search(
        string name,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    )
    {
        var query = Query(name, maxResults);
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

    // GLEIF's autocomplete over the same names, which puts a brand's own entity first where full-text does not
    // ("Google" reads GOOGLE LLC first). A suggestion without an LEI is skipped; entities keep the suggestions' order.
    public virtual async Task<List<GleifEntity>> Suggest(
        string name,
        int maxResults = 10,
        CancellationToken cancellationToken = default
    )
    {
        var query = Query(name, maxResults);
        var source = new Uri(
            GleifHttp.Origin,
            "/api/v1/autocompletions?field=fulltext&q=" + Uri.EscapeDataString(query)
        );
        var body = await GleifHttp.Read(_httpClient, source, _logger, cancellationToken);
        var leis = Parse(
            body,
            "GLEIF autocompletion response has an invalid structure.",
            root =>
            {
                var data = root.GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("GLEIF data must be an array.");
                var result = new List<string>();
                foreach (var suggestion in data.EnumerateArray())
                {
                    var lei = SuggestedLei(suggestion);
                    if (lei != null && !result.Contains(lei))
                        result.Add(lei);
                }
                return result;
            }
        );
        leis = leis.Take(maxResults).ToList();
        if (leis.Count == 0)
            return [];
        var entities = (await GetEntities(leis, cancellationToken)).ToDictionary(entity =>
            entity.LegalEntityIdentifier
        );
        return leis.Where(entities.ContainsKey).Select(lei => entities[lei]).ToList();
    }

    // Several LEI records in one request. GLEIF returns them in its own order and leaves out an LEI it does not hold.
    public virtual async Task<List<GleifEntity>> GetEntities(
        IReadOnlyCollection<string> leis,
        CancellationToken cancellationToken = default
    )
    {
        var requested = (leis ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (requested.Count is < 1 or > MaxSearchResults)
            throw new ArgumentOutOfRangeException(nameof(leis));
        foreach (var lei in requested)
            RequireLei(lei);
        var source = new Uri(
            GleifHttp.Origin,
            "/api/v1/lei-records?filter%5Blei%5D="
                + string.Join(",", requested)
                + "&page%5Bsize%5D="
                + requested.Count
        );
        var body = await GleifHttp.Read(_httpClient, source, _logger, cancellationToken);
        return Parse(
            body,
            "GLEIF legal entity list has an invalid structure.",
            root =>
            {
                var data = root.GetProperty("data");
                if (data.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("GLEIF data must be an array.");
                var pending = requested.ToHashSet(StringComparer.Ordinal);
                var result = new List<GleifEntity>();
                foreach (var record in data.EnumerateArray())
                {
                    var entity = Entity(record, null);
                    if (!pending.Remove(entity.LegalEntityIdentifier))
                        throw new InvalidDataException(
                            "GLEIF returned an LEI record that was not requested."
                        );
                    result.Add(entity);
                }
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

    // The entity's direct accounting parent, or null when GLEIF records none. The LEI must come from a GLEIF
    // record: GLEIF answers 404 both for an entity without a parent and for an LEI it does not hold.
    public virtual Task<GleifEntity> GetDirectParent(
        string lei,
        CancellationToken cancellationToken = default
    ) => GetParent(lei, "direct-parent", cancellationToken);

    // The top of the entity's consolidation chain, or null when GLEIF records none (Alphabet has no parent);
    // the same LEI rule as GetDirectParent applies.
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

    // Commas go first: GLEIF reads one inside a filter value as a list separator, so "Co., Ltd." returned every
    // entity named "Ltd".
    private static string Query(string name, int maxResults)
    {
        var query = Spaces().Replace((name ?? "").Replace(',', ' '), " ").Trim();
        if (query.Length == 0 || query.Length > MaxQueryLength)
            throw new ArgumentException("A name of 1 to 200 characters is required.", nameof(name));
        if (maxResults is < 1 or > MaxSearchResults)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        return query;
    }

    // The LEI a suggestion links to, or null for a suggested name GLEIF ties to no single record.
    private static string SuggestedLei(JsonElement suggestion)
    {
        if (
            !suggestion.TryGetProperty("relationships", out var relationships)
            || relationships.ValueKind != JsonValueKind.Object
            || !relationships.TryGetProperty("lei-records", out var records)
            || !records.TryGetProperty("data", out var record)
            || record.ValueKind != JsonValueKind.Object
        )
            return null;
        var lei = Text(record, "id");
        if (
            Text(record, "type") != "lei-records"
            || !InternationalSecurityIdentifiers.IsValidLei(lei)
        )
            throw new InvalidDataException("GLEIF suggestion names an invalid LEI.");
        return lei;
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
