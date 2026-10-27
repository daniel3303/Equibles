using System.Globalization;
using System.Text.Json;
using Equibles.Integrations.Common.Http;
using Equibles.Integrations.Lse.Models;

namespace Equibles.Integrations.Lse;

// The exchange's reports page is a script application that lists its current reports from one JSON index on the
// document host; the instrument list is the one entry of its report type, and the index states where it lives.
public class LseInstrumentListClient(HttpClient httpClient)
{
    public static readonly Uri PublisherPage = new(
        "https://www.londonstockexchange.com/reports?tab=instruments"
    );

    internal static readonly Uri Origin = new("https://docs.londonstockexchange.com");
    internal static readonly Uri ReportIndex = new(Origin, "reports/instruments/latest.json");
    internal const string ReportType = "latest-instrument-list";
    private const int MaxIndexBytes = 1_000_000;
    private const int MaxWorkbookBytes = 64_000_000;
    private const long MaxPartBytes = 64_000_000;

    public async Task<LseInstrumentList> GetInstruments(
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var (source, publishedOn) = await FindLatest(timeout.Token);
        var payload = await SameOriginBinaryReader.Read(
            httpClient,
            Origin,
            source,
            MaxWorkbookBytes,
            timeout.Token
        );
        var list = LseInstrumentListParser.Read(payload.Bytes, MaxPartBytes);
        list.PageUrl = PublisherPage;
        list.SourceUrl = source;
        list.PublishedOn = publishedOn;
        list.CapturedAt = DateTime.UtcNow;
        return list;
    }

    // Exactly one instrument list, on the document host, with its publication date; anything else is a changed
    // publisher to re-verify, never an empty market.
    internal async Task<(Uri Source, DateOnly PublishedOn)> FindLatest(
        CancellationToken cancellationToken
    )
    {
        var index = await SameOriginBinaryReader.Read(
            httpClient,
            Origin,
            ReportIndex,
            MaxIndexBytes,
            cancellationToken,
            "application/json"
        );
        List<JsonElement> entries;
        try
        {
            using var document = JsonDocument.Parse(index.Bytes);
            entries =
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Data", out var data)
                && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray()
                        .Where(entry =>
                            entry.ValueKind == JsonValueKind.Object
                            && Text(entry, "reportType") == ReportType
                        )
                        .Select(entry => entry.Clone())
                        .ToList()
                    : [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The London report index is not JSON.", exception);
        }
        if (entries.Count != 1)
            throw new InvalidDataException(
                $"The London report index names {entries.Count} instrument lists, not one."
            );
        var link = Text(entries[0], "downloadLink");
        if (
            !Uri.TryCreate(link, UriKind.Absolute, out var source)
            || !SameOriginBinaryReader.IsOnOrigin(Origin, source)
        )
            throw new InvalidDataException(
                $"The London instrument list is not on {Origin.Host}: {link}"
            );
        if (
            !DateOnly.TryParseExact(
                Text(entries[0], "publishedDate"),
                "dd-MM-yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var publishedOn
            )
        )
            throw new InvalidDataException(
                "The London instrument list states no publication date."
            );
        return (source, publishedOn);
    }

    private static string Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
