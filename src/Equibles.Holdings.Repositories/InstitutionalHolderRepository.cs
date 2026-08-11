using Equibles.CommonStocks.Data.Helpers;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Repositories;

public class InstitutionalHolderRepository : BaseRepository<InstitutionalHolder>
{
    public InstitutionalHolderRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public async Task<InstitutionalHolder> GetByCik(string cik)
    {
        var validatedCik = CikNormalizer.Validate(cik);
        return validatedCik == null
            ? null
            : await GetAll().FirstOrDefaultAsync(h => h.Cik == validatedCik);
    }

    public IQueryable<InstitutionalHolder> GetByCiks(IEnumerable<string> ciks)
    {
        var rawCiks = ciks?.ToList() ?? [];
        var validatedCiks = rawCiks.Select(CikNormalizer.Validate).ToList();
        return validatedCiks.Any(cik => cik == null)
            ? GetAll().Where(_ => false)
            : GetAll().Where(h => validatedCiks.Contains(h.Cik));
    }

    public IQueryable<InstitutionalHolder> Search(string search)
    {
        var matches = SearchNameTokens(search, requireAll: true);
        var aliasCik = InstitutionalHolderSearchAliases.ResolveCik(search);
        var strict =
            aliasCik == null
                ? matches
                : matches.Concat(GetAll().Where(h => h.Cik == aliasCik)).Distinct();
        return SearchTerms.WithSparseAnyTokenFallback(
            strict,
            SearchNameTokens(search, requireAll: false)
        );
    }

    // Typeahead variant: matches a CIK prefix as well as a name substring so the
    // picker can resolve either "berk" or "1067983" to the same row. The user's
    // input is escaped first so '%' / '_' / '\\' in the query don't behave as LIKE
    // wildcards (e.g. "50%" would otherwise match every name).
    public IQueryable<InstitutionalHolder> SearchNameOrCik(string search)
    {
        return SearchTerms.WithSparseAnyTokenFallback(
            SearchNameOrCikStrict(search),
            SearchNameTokens(search, requireAll: false)
        );
    }

    // Resolution must never discard an unmatched word through discovery's any-token fallback:
    // "wrong Berkshire" is not an exact or unique all-token identity. Scoped tools call this
    // strict shape through ResolveNameOrCik and fail closed when it has multiple candidates.
    private IQueryable<InstitutionalHolder> SearchNameOrCikStrict(string search)
    {
        var nameMatches = SearchNameTokens(search, requireAll: true);
        var exactCik = NormalizeExactCikQuery(search);
        var identityMatches = GetAll().Where(_ => false);
        if (exactCik != null)
        {
            var primaryCikPrefix = $"{EscapeLikePattern(exactCik)}%";
            var alternateCik = AlternateCikSpelling(exactCik);
            var alternateCikPrefix =
                alternateCik == null ? null : $"{EscapeLikePattern(alternateCik)}%";
            identityMatches = GetAll()
                .Where(h =>
                    EF.Functions.ILike(h.Cik, primaryCikPrefix, LikePattern.EscapeChar)
                    || (
                        alternateCikPrefix != null
                        && EF.Functions.ILike(h.Cik, alternateCikPrefix, LikePattern.EscapeChar)
                    )
                );
        }

        var strict = nameMatches.Concat(identityMatches);
        var aliasCik = InstitutionalHolderSearchAliases.ResolveCik(search);
        if (aliasCik != null)
            strict = strict.Concat(GetAll().Where(h => h.Cik == aliasCik));
        return strict.Distinct();
    }

    private IQueryable<InstitutionalHolder> SearchNameTokens(string search, bool requireAll)
    {
        var tokens = SearchTerms.Tokenize(search);
        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(search))
            return GetAll().Where(_ => false);
        if (tokens.Count == 0)
            return requireAll ? GetAll() : GetAll().Where(_ => false);

        if (requireAll)
        {
            var matches = GetAll();
            foreach (var token in tokens)
            {
                var pattern = LikePattern.Contains(token);
                matches = matches.Where(h =>
                    EF.Functions.ILike(h.Name, pattern, LikePattern.EscapeChar)
                );
            }

            return matches;
        }

        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            var pattern = LikePattern.Contains(token);
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll().Where(h => EF.Functions.ILike(h.Name, pattern, LikePattern.EscapeChar))
            );
        }

        return anyTokenMatches.Distinct();
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
    ) =>
        (await SearchNameOrCikLargestFirstWithStats(search, maxResults, cancellationToken))
            .Select(m => m.Holder)
            .ToList();

    public async Task<List<InstitutionalHolderSearchMatch>> SearchNameOrCikLargestFirstWithStats(
        string search,
        int maxResults,
        CancellationToken cancellationToken = default
    ) => await RankWithStats(SearchNameOrCik(search), maxResults, cancellationToken);

    private async Task<List<InstitutionalHolderSearchMatch>> RankWithStats(
        IQueryable<InstitutionalHolder> source,
        int maxResults,
        CancellationToken cancellationToken
    )
    {
        var matches = await source.Select(h => new { h.Id, h.Name }).ToListAsync(cancellationToken);
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

        // One row per holder, aggregated in SQL: the newest reported quarter, with every
        // accession rollup in that quarter summed. A NEW HOLDINGS amendment deliberately leaves
        // disjoint positions split between the original and amendment accessions, so taking one
        // accession (or Max) understates both AUM and breadth. A grouped-subquery join keeps a
        // broad match set from materialising every (holder, quarter) pair client-side.
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
                SizeAtLatest = g.Sum(f => f.TotalValue),
                PositionCountAtLatest = g.Sum(f => f.PositionCount),
            }
        ).ToListAsync(cancellationToken);

        var statsByHolder = stats.ToDictionary(
            s => s.Id,
            s => (s.LatestReportDate, s.SizeAtLatest, s.PositionCountAtLatest)
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
        var holdersById = holders.ToDictionary(h => h.Id);
        return topIds
            .Select(id =>
            {
                var hasStats = statsByHolder.TryGetValue(id, out var holderStats);
                return new InstitutionalHolderSearchMatch
                {
                    Holder = holdersById[id],
                    LatestReportDate = hasStats ? holderStats.LatestReportDate : null,
                    ReportedAum = hasStats ? holderStats.SizeAtLatest : null,
                    PositionCount = hasStats ? holderStats.PositionCountAtLatest : null,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Resolves an exact legal name, exact CIK, or verified brand alias. A unique partial match
    /// also resolves; multiple partial matches remain explicit candidates so callers cannot
    /// silently run a portfolio query against the wrong filer.
    /// </summary>
    public async Task<InstitutionalHolderResolution> ResolveNameOrCik(
        string search,
        int maxCandidates = 5,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = SearchTerms.Normalize(search);
        var exactCik = NormalizeExactCikQuery(search);
        if (exactCik != null)
        {
            var identity = await RankWithStats(
                GetAll().Where(h => h.Cik == exactCik),
                2,
                cancellationToken
            );
            if (identity.Count == 1)
            {
                return new InstitutionalHolderResolution
                {
                    Selected = identity[0],
                    Candidates = identity,
                };
            }

            var alternateCik = AlternateCikSpelling(exactCik);
            if (alternateCik != null)
            {
                var alternate = await RankWithStats(
                    GetAll().Where(h => h.Cik == alternateCik),
                    2,
                    cancellationToken
                );
                if (alternate.Count == 1)
                {
                    return new InstitutionalHolderResolution
                    {
                        Selected = alternate[0],
                        Candidates = alternate,
                    };
                }
            }
        }

        var nameCandidates = await SearchNameOrCikStrict(search ?? string.Empty)
            .Select(h => new { h.Id, h.Name })
            .ToListAsync(cancellationToken);
        var trimmed = search?.Trim();
        var literalExactIds = nameCandidates
            .Where(m => string.Equals(m.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Id)
            .ToList();
        if (literalExactIds.Count > 0)
            return await BuildExactResolution(literalExactIds, maxCandidates, cancellationToken);

        var normalizedExactIds = nameCandidates
            .Where(m => SearchTerms.Normalize(m.Name) == normalized)
            .Select(m => m.Id)
            .ToList();
        if (normalizedExactIds.Count > 0)
            return await BuildExactResolution(normalizedExactIds, maxCandidates, cancellationToken);

        var aliasCik = InstitutionalHolderSearchAliases.ResolveCik(search);
        if (aliasCik != null)
        {
            var alias = await RankWithStats(
                GetAll().Where(h => h.Cik == aliasCik),
                2,
                cancellationToken
            );
            if (alias.Count == 1)
            {
                return new InstitutionalHolderResolution
                {
                    Selected = alias[0],
                    Candidates = alias,
                };
            }
        }

        var candidateIds = nameCandidates.Select(m => m.Id).ToList();
        var ranked = await RankWithStats(
            GetAll().Where(h => candidateIds.Contains(h.Id)),
            Math.Max(25, maxCandidates),
            cancellationToken
        );
        if (ranked.Count == 0)
            return new InstitutionalHolderResolution();

        if (ranked.Count == 1)
        {
            return new InstitutionalHolderResolution { Selected = ranked[0], Candidates = ranked };
        }

        return new InstitutionalHolderResolution
        {
            Candidates = ranked.Take(Math.Max(1, maxCandidates)).ToList(),
        };
    }

    private async Task<InstitutionalHolderResolution> BuildExactResolution(
        List<Guid> exactIds,
        int maxCandidates,
        CancellationToken cancellationToken
    )
    {
        var exact = await RankWithStats(
            GetAll().Where(h => exactIds.Contains(h.Id)),
            Math.Max(1, maxCandidates),
            cancellationToken
        );
        return new InstitutionalHolderResolution
        {
            Selected = exactIds.Count == 1 ? exact[0] : null,
            Candidates = exact.Take(Math.Max(1, maxCandidates)).ToList(),
        };
    }

    private static string NormalizeExactCikQuery(string search) => CikNormalizer.Validate(search);

    // Both padded and unpadded CIK spellings exist in the holder table. Exact input wins;
    // this alternate is consulted only when the exact spelling has no row.
    private static string AlternateCikSpelling(string exactCik)
    {
        if (exactCik == null)
            return null;

        var unpadded = NormalizeCikQuery(exactCik);
        if (unpadded != exactCik)
            return unpadded;
        if (exactCik.All(char.IsAsciiDigit) && exactCik.Any(c => c != '0'))
        {
            var padded = exactCik.PadLeft(10, '0');
            return padded == exactCik ? null : padded;
        }

        return null;
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

internal static class InstitutionalHolderSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> Ciks = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        // Fidelity's flagship 13F filer is legally named FMR LLC, so the brand
        // word does not occur in its filed name.
        ["fidelity"] = "315066",
        ["fmr fidelity"] = "315066",
        ["fidelity fmr"] = "315066",
        ["vanguard"] = "102909",
        ["vanguard group"] = "102909",
        ["vanguard group inc"] = "102909",
        ["blackrock"] = "2012383",
        ["blackrock inc"] = "2012383",
    };

    public static string ResolveCik(string query) =>
        Ciks.GetValueOrDefault(SearchTerms.Normalize(query));
}
