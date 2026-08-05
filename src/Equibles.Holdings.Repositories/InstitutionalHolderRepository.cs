using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHolderRepository : BaseRepository<InstitutionalHolder>
{
    public InstitutionalHolderRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InstitutionalHolder> GetByCik(string cik)
    {
        return await GetAll().FirstOrDefaultAsync(h => h.Cik == cik);
    }

    public IQueryable<InstitutionalHolder> GetByCiks(IEnumerable<string> ciks)
    {
        return GetAll().Where(h => ciks.Contains(h.Cik));
    }

    public IQueryable<InstitutionalHolder> Search(string search)
    {
        // "Name contains the term"; escape so '%' / '_' / '\' match literally, matching SearchNameOrCik.
        var pattern = LikePattern.Contains(search);
        return GetAll().Where(h => EF.Functions.ILike(h.Name, pattern, LikePattern.EscapeChar));
    }

    // Typeahead variant: matches a CIK prefix as well as a name substring so the
    // picker can resolve either "berk" or "1067983" to the same row. The user's
    // input is escaped first so '%' / '_' / '\\' in the query don't behave as LIKE
    // wildcards (e.g. "50%" would otherwise match every name).
    public IQueryable<InstitutionalHolder> SearchNameOrCik(string search)
    {
        var escaped = EscapeLikePattern(search);
        var namePattern = $"%{escaped}%";
        var cikPrefix = $"{EscapeLikePattern(NormalizeCikQuery(search))}%";
        return GetAll()
            .Where(h =>
                EF.Functions.ILike(h.Name, namePattern, LikePattern.EscapeChar)
                || EF.Functions.ILike(h.Cik, cikPrefix, LikePattern.EscapeChar)
            );
    }

    // CIKs are stored unpadded but SEC-canonical form zero-pads them to 10 digits (EDGAR and
    // most sources hand out '0001067983', not '1067983'), so an all-digit query strips its
    // leading zeros before becoming the CIK prefix. An all-zero query keeps the original
    // input — trimming it to empty would turn the prefix into a match-everything '%'.
    private static string NormalizeCikQuery(string search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.All(char.IsAsciiDigit))
            return search;
        var unpadded = trimmed.TrimStart('0');
        return unpadded.Length == 0 ? search : unpadded;
    }

    // A filer whose newest 13F quarter is within this window of the newest 13F quarter among
    // the matches is "live". The threshold compares report dates to report dates (never the
    // wall clock), so the effective rule is "at most one quarter behind the newest match":
    // consecutive quarter ends sit 90-92 days apart and two quarters sit 181-184 apart, and any
    // constant between those bands behaves identically. Mid filing season the flagship may not
    // have filed the newest quarter yet, and must not lose its bucket to a smaller filer that
    // filed a few days earlier.
    private const int LiveFilerWindowDays = 135;

    // Resolves a name/CIK query to filers ranked live-and-largest-first. Recency comes before
    // size: corporate re-registrations leave the old CIK dormant with its giant historical
    // filings intact, and pure size ranking then resolves the household name to the dead entity
    // forever — "BlackRock" answered a two-year-stale portfolio off the retired filer while the
    // live successor CIK filed on schedule. Within a bucket, size at the LATEST quarter ranks
    // (not the all-time maximum, which rewards a filer for a past it no longer has); a bare
    // famous name must still resolve to the flagship filer, not a small same-named RIA
    // ("Bridgewater" → Bridgewater Associates, LP, never Bridgewater Advisors Inc.). Filers
    // with no 13F rollup rows (13D/G-only filers) rank last; ties break on name length then
    // name so an exact name still beats longer decorated variants at equal size. The live
    // anchor is the newest quarter among the MATCHES, so the same filer can rank live for one
    // query and dormant for a narrower one — deliberate, so a defunct name still ranks.
    public async Task<List<InstitutionalHolder>> SearchNameOrCikLargestFirst(
        string search,
        int maxResults,
        CancellationToken cancellationToken = default
    )
    {
        var matches = await SearchNameOrCik(search)
            .Select(h => new { h.Id, h.Name })
            .ToListAsync(cancellationToken);
        if (matches.Count == 0)
            return [];

        var ids = matches.Select(m => m.Id).ToList();
        // 13F rows ONLY: the rollup also carries Schedule 13D/G rows, whose event dates are
        // always fresher than the last quarter end and whose TotalValue is one stake — ranked
        // unfiltered, "Millennium" resolves to whichever namesake filed a 13D/G most recently
        // instead of the $79B flagship. (Rows predating the FilingType column default to 13F
        // until the worker's backfill restamps them — the pre-existing failure mode, healed
        // within its first pass.)
        var thirteenFRollups = DbContext
            .Set<InstitutionalFiling>()
            .Where(f =>
                ids.Contains(f.InstitutionalHolderId) && f.FilingType == FilingType.Form13F
            );

        // One row per holder, aggregated in SQL: the newest reported quarter, and the size of
        // the largest filing IN that quarter (an amendment and its original are separate rollup
        // rows; the largest is the fuller picture). A grouped-subquery join keeps a broad match
        // set from materialising every (holder, quarter) pair client-side.
        var latestQuarters = thirteenFRollups
            .GroupBy(f => f.InstitutionalHolderId)
            .Select(g => new { Id = g.Key, Latest = g.Max(f => f.ReportDate) });
        var stats = await (
            from f in thirteenFRollups
            join l in latestQuarters
                on new { Id = f.InstitutionalHolderId, Date = f.ReportDate } equals new
                {
                    l.Id,
                    Date = l.Latest,
                }
            group f by f.InstitutionalHolderId into g
            select new
            {
                Id = g.Key,
                LatestReportDate = g.Max(f => f.ReportDate),
                SizeAtLatest = g.Max(f => f.TotalValue),
            }
        ).ToListAsync(cancellationToken);

        var statsByHolder = stats.ToDictionary(
            s => s.Id,
            s => (s.LatestReportDate, s.SizeAtLatest)
        );

        // The live window anchors on the newest quarter among the MATCHES, so an all-dormant
        // match set (a genuinely defunct name) still ranks sensibly instead of losing its
        // bucket to an empty threshold.
        var liveThreshold =
            statsByHolder.Count > 0
                ? statsByHolder.Values.Max(s => s.LatestReportDate).AddDays(-LiveFilerWindowDays)
                : DateOnly.MinValue;

        var topIds = matches
            .OrderByDescending(m =>
                statsByHolder.TryGetValue(m.Id, out var s) && s.LatestReportDate >= liveThreshold
            )
            .ThenByDescending(m =>
                statsByHolder.TryGetValue(m.Id, out var s) ? s.SizeAtLatest : -1L
            )
            .ThenBy(m => m.Name.Length)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(m => m.Id)
            .ToList();

        var holders = await GetAll()
            .Where(h => topIds.Contains(h.Id))
            .ToListAsync(cancellationToken);
        return topIds.Select(id => holders.First(h => h.Id == id)).ToList();
    }

    // Local alias over the shared escaper, kept so SearchNameOrCik reads as one concept.
    private static string EscapeLikePattern(string input) => LikePattern.Escape(input);

    // Distinct non-empty state/country codes across the filer universe, used to
    // populate the location filter dropdown on the institutions index so the user
    // picks from values that actually exist rather than typing a free-form code.
    public IQueryable<string> DistinctStatesOrCountries()
    {
        return GetAll()
            .Where(h => h.StateOrCountry != null && h.StateOrCountry != "")
            .Select(h => h.StateOrCountry)
            .Distinct();
    }

    public IQueryable<InstitutionalHolder> GetUnclassified()
    {
        return GetAll()
            .Where(h => h.Classification == FundClassification.Unknown && h.Name != null);
    }
}
