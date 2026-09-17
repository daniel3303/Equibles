using System.Globalization;
using System.Text.Json;
using Equibles.Integrations.XbrlFilings.Models;

namespace Equibles.Integrations.XbrlFilings;

// Reads the host's JSON:API index. The filer's identity comes from the included entity resource, never from
// the composite key, and every address is copied as stated.
public static class XbrlFilingsParser
{
    // The index states no regime field; the regime is named inside the filing key the host builds,
    // `{identifier}-{period}-{regime}-{country}-{index}`, and in the stored path beside it.
    public const string EsefRegime = "ESEF";

    public static XbrlFilingPage Read(string json, Uri origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The filing index served an empty reply.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data))
            throw new InvalidDataException("The filing index reply carries no data array.");
        if (data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The filing index data is not an array.");
        var entities = ReadEntities(root);
        var filings = new List<XbrlFiling>();
        foreach (var row in data.EnumerateArray())
        {
            var filing = ReadFiling(row, entities, origin);
            if (filing != null)
                filings.Add(filing);
        }
        return new XbrlFilingPage(filings, ReadTotal(root));
    }

    // The `included` array carries one entity resource per distinct filer on the page; a filing points at it
    // by resource id, so the identifier is read from the entity the host states rather than parsed out of a key.
    private static Dictionary<string, (string Identifier, string Name)> ReadEntities(
        JsonElement root
    )
    {
        var entities = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (
            !root.TryGetProperty("included", out var included)
            || included.ValueKind != JsonValueKind.Array
        )
            return entities;
        foreach (var resource in included.EnumerateArray())
        {
            if (Text(resource, "type") != "entity")
                continue;
            var id = Text(resource, "id");
            if (id == null || !resource.TryGetProperty("attributes", out var attributes))
                continue;
            entities[id] = (Text(attributes, "identifier"), Text(attributes, "name"));
        }
        return entities;
    }

    private static XbrlFiling ReadFiling(
        JsonElement row,
        Dictionary<string, (string Identifier, string Name)> entities,
        Uri origin
    )
    {
        if (!row.TryGetProperty("attributes", out var attributes))
            return null;
        var entityId = EntityResourceId(row);
        if (entityId == null || !entities.TryGetValue(entityId, out var entity))
            return null;
        if (string.IsNullOrWhiteSpace(entity.Identifier))
            return null;
        var key = Text(attributes, "fxo_id");
        return new XbrlFiling
        {
            EntityIdentifier = entity.Identifier,
            EntityName = entity.Name,
            Regime = Regime(key),
            CountryCode = Text(attributes, "country"),
            PeriodEnd = Date(Text(attributes, "period_end")),
            AddedAt = Timestamp(Text(attributes, "date_added")),
            ErrorCount = Count(attributes, "error_count"),
            Sha256 = Text(attributes, "sha256"),
            ReportUrl = Address(origin, Text(attributes, "report_url")),
            PackageUrl = Address(origin, Text(attributes, "package_url")),
            JsonUrl = Address(origin, Text(attributes, "json_url")),
            FilingKey = key,
        };
    }

    private static string EntityResourceId(JsonElement row) =>
        row.TryGetProperty("relationships", out var relationships)
        && relationships.TryGetProperty("entity", out var entity)
        && entity.TryGetProperty("data", out var data)
            ? Text(data, "id")
            : null;

    // The regime is the third hyphen-separated part from the END of the key. It is read from that end
    // because the identifier ahead of it may itself carry a hyphen, while the country and the index after
    // it are each one part.
    private static string Regime(string filingKey)
    {
        if (string.IsNullOrWhiteSpace(filingKey))
            return null;
        var segments = filingKey.Split('-');
        return segments.Length < 5 ? null : segments[^3];
    }

    private static Uri Address(Uri origin, string stated) =>
        string.IsNullOrWhiteSpace(stated) ? null
        : Uri.TryCreate(origin, stated, out var address) ? address
        : null;

    private static DateOnly? Date(string stated) =>
        DateOnly.TryParseExact(
            stated,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var value
        )
            ? value
            : null;

    private static DateTime? Timestamp(string stated) =>
        DateTime.TryParse(
            stated,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var value
        )
            ? value
            : null;

    private static int Count(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var count)
            ? count
            : 0;

    private static int ReadTotal(JsonElement root) =>
        root.TryGetProperty("meta", out var meta) ? Count(meta, "count") : 0;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
