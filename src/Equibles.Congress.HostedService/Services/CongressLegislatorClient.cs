using System.Text.Json;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Reads the public congress-legislators roster (CC0): every legislator's BioGuide id, name and
/// terms. The files are streamed, and each term is stored half-open, split at party switches.
/// </summary>
[Service]
public class CongressLegislatorClient
{
    internal const string CurrentUrl =
        "https://unitedstates.github.io/congress-legislators/legislators-current.json";
    internal const string HistoricalUrl =
        "https://unitedstates.github.io/congress-legislators/legislators-historical.json";

    // Trade disclosures start with the STOCK Act in 2012; older careers are never matched.
    internal static readonly DateOnly EarliestTermEnd = new(2012, 1, 1);

    private const int MaxRetries = 3;

    private static readonly IRateLimiter RateLimiter = new RateLimiter(
        maxRequests: 5,
        timeWindow: TimeSpan.FromSeconds(1)
    );

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CongressLegislatorClient> _logger;

    public CongressLegislatorClient(HttpClient httpClient, ILogger<CongressLegislatorClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<LegislatorRosterParseResult> GetCurrent(CancellationToken ct) =>
        Fetch(CurrentUrl, ct);

    public Task<LegislatorRosterParseResult> GetHistorical(CancellationToken ct) =>
        Fetch(HistoricalUrl, ct);

    private async Task<LegislatorRosterParseResult> Fetch(string url, CancellationToken ct)
    {
        using var response = await HttpRetry.Send(
            () =>
                _httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url),
                    HttpCompletionOption.ResponseHeadersRead,
                    ct
                ),
            RateLimiter,
            MaxRetries,
            $"Legislator roster {url} kept failing",
            (attempt, delay) =>
                _logger.LogWarning(
                    "Legislator roster throttled (attempt {Attempt}), retrying in {Delay}",
                    attempt + 1,
                    delay
                ),
            (status, attempt, delay) =>
                _logger.LogWarning(
                    "Legislator roster returned {Status} (attempt {Attempt}), retrying in {Delay}",
                    status,
                    attempt + 1,
                    delay
                ),
            ct
        );
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ParseLegislators(stream, ct);
    }

    /// <summary>
    /// Reads one roster file. A legislator whose terms all ended before 2012 is skipped; one whose
    /// record cannot be stored faithfully is rejected whole, never stored in part.
    /// </summary>
    internal static async Task<LegislatorRosterParseResult> ParseLegislators(
        Stream stream,
        CancellationToken ct = default
    )
    {
        var result = new LegislatorRosterParseResult();
        await foreach (
            var record in JsonSerializer.DeserializeAsyncEnumerable<LegislatorRecord>(
                stream,
                SerializerOptions,
                ct
            )
        )
        {
            if (record == null)
                continue;

            result.RecordCount++;
            if (!record.Terms.Any(term => term?.End >= EarliestTermEnd))
                continue;

            var rejection = TryBuildLegislator(record, out var legislator);
            if (rejection != null)
                result.Rejections.Add(rejection);
            else
                result.Legislators.Add(legislator);
        }

        return result;
    }

    private static LegislatorRejection TryBuildLegislator(
        LegislatorRecord record,
        out CongressLegislator legislator
    )
    {
        legislator = null;
        var bioguideId = record.Id?.Bioguide?.Trim();
        if (string.IsNullOrEmpty(bioguideId) || bioguideId.Length > 7)
            return new LegislatorRejection(bioguideId, "missing or malformed BioGuide id");

        LegislatorRejection Reject(string reason) => new(bioguideId, reason);

        var name = record.Name;
        if (
            string.IsNullOrWhiteSpace(name?.First)
            || string.IsNullOrWhiteSpace(name.Last)
            || name.First.Trim().Length > 128
            || name.Last.Trim().Length > 128
            || name.Suffix?.Trim().Length > 16
            || name.OfficialFull?.Trim().Length > 256
        )
            return Reject("missing or oversized name");

        var terms = new List<CongressMemberTerm>();
        foreach (var term in record.Terms)
        {
            var reason = TrySplitTerm(bioguideId, term, terms);
            if (reason != null)
                return Reject(reason);
        }

        var ordered = terms.OrderBy(term => term.StartDate).ToList();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            // A chamber switch can overlap by the day of the swearing-in; the later term wins it.
            if (ordered[i].EndDate > ordered[i + 1].StartDate)
                ordered[i].EndDate = ordered[i + 1].StartDate;
            if (ordered[i].EndDate <= ordered[i].StartDate)
                return Reject($"terms starting {ordered[i].StartDate:O} overlap");
        }

        legislator = new CongressLegislator
        {
            BioguideId = bioguideId,
            FirstName = name.First.Trim(),
            LastName = name.Last.Trim(),
            Suffix = Blank(name.Suffix),
            OfficialFullName = Blank(name.OfficialFull),
            Terms = ordered,
        };
        return null;
    }

    private static string TrySplitTerm(
        string bioguideId,
        LegislatorTermRecord term,
        List<CongressMemberTerm> terms
    )
    {
        if (term?.Start == null || term.End == null || term.End <= term.Start)
            return "a term has missing or inverted dates";

        CongressPosition chamber;
        short? district;
        switch (term.Type)
        {
            case "rep":
                if (term.District is not (>= 0 and <= short.MaxValue))
                    return $"House term starting {term.Start:O} has no district";
                chamber = CongressPosition.Representative;
                district = (short)term.District.Value;
                break;
            case "sen":
                chamber = CongressPosition.Senator;
                district = null;
                break;
            default:
                return $"term starting {term.Start:O} has unknown type '{term.Type}'";
        }

        var state = term.State?.Trim().ToUpperInvariant();
        if (state is not { Length: 2 })
            return $"term starting {term.Start:O} has no two-letter state";

        var segments = SplitByParty(term);
        if (segments == null)
            return $"party affiliations of the term starting {term.Start:O} do not cover it";

        foreach (var (start, end, party) in segments)
        {
            var trimmedParty = party?.Trim();
            if (string.IsNullOrEmpty(trimmedParty) || trimmedParty.Length > 32)
                return $"term starting {start:O} has no party";
            if (term.Caucus?.Trim().Length > 32)
                return $"term starting {start:O} has an oversized caucus";

            terms.Add(
                new CongressMemberTerm
                {
                    BioguideId = bioguideId,
                    Chamber = chamber,
                    State = state,
                    District = district,
                    Party = trimmedParty,
                    Caucus = Blank(term.Caucus),
                    StartDate = start,
                    EndDate = end,
                }
            );
        }

        return null;
    }

    /// <summary>
    /// Splits a term at its party switches. The roster writes a switch either as one segment
    /// ending the day the next starts or, inclusively, the day before; both meet at the next
    /// segment's start. Segments that leave any other gap, overlap, or miss the term's own start
    /// or end return null.
    /// </summary>
    private static List<(DateOnly Start, DateOnly End, string Party)> SplitByParty(
        LegislatorTermRecord term
    )
    {
        var start = term.Start!.Value;
        var end = term.End!.Value;
        if (term.PartyAffiliations.Count == 0)
            return [(start, end, term.Party)];

        var affiliations = term.PartyAffiliations;
        if (
            affiliations.Any(a => a?.Start == null || a.End == null)
            || affiliations[0].Start != start
            || affiliations[^1].End != end
        )
            return null;

        var segments = new List<(DateOnly, DateOnly, string)>();
        for (var i = 0; i < affiliations.Count; i++)
        {
            var segmentStart = affiliations[i].Start!.Value;
            var segmentEnd = end;
            if (i < affiliations.Count - 1)
            {
                var nextStart = affiliations[i + 1].Start!.Value;
                var filedEnd = affiliations[i].End!.Value;
                if (filedEnd != nextStart && filedEnd.AddDays(1) != nextStart)
                    return null;
                segmentEnd = nextStart;
            }

            if (segmentEnd <= segmentStart)
                return null;
            segments.Add((segmentStart, segmentEnd, affiliations[i].Party));
        }

        return segments;
    }

    private static string Blank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
