using Equibles.Data;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories;

/// <summary>
/// Reads the materialised fund directory (<see cref="FundSeries"/>). One row per registered-fund
/// series, rebuilt by the refresh worker — these queries are plain indexed lookups, never the live
/// "latest report per series" scan over <see cref="NportFiling"/>.
/// </summary>
public class FundSeriesRepository : BaseRepository<FundSeries>
{
    public FundSeriesRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    /// <summary>The series with the given route slug (empty result when unknown).</summary>
    public IQueryable<FundSeries> GetBySlug(string slug)
    {
        return GetAll().Where(s => s.Slug == slug);
    }

    /// <summary>
    /// Finds fund series by punctuation-independent tokens across series name, registrant, and
    /// ticker. Exact stored identifiers win, followed by a verified share-class alias, all-token
    /// matches, then a sparse any-token fallback. Share-class aliases resolve even when N-PORT
    /// does not carry that class ticker.
    /// </summary>
    public IQueryable<FundSeries> Search(string query)
    {
        var tokens = SearchTerms.Tokenize(query);
        if (tokens.Count == 0 && !string.IsNullOrWhiteSpace(query))
            return GetAll().Where(_ => false);

        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            matches = matches.Where(f =>
                (f.SeriesName != null && f.SeriesName.ToLower().Contains(token))
                || (f.RegistrantName != null && f.RegistrantName.ToLower().Contains(token))
                || (f.Ticker != null && f.Ticker.ToLower().Contains(token))
            );
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll()
                    .Where(f =>
                        (f.SeriesName != null && f.SeriesName.ToLower().Contains(token))
                        || (f.RegistrantName != null && f.RegistrantName.ToLower().Contains(token))
                        || (f.Ticker != null && f.Ticker.ToLower().Contains(token))
                    )
            );
        }

        var exact = query.Trim().ToLowerInvariant();
        var exactIdentifier = GetAll()
            .Where(f =>
                f.SeriesId.ToLower() == exact
                || (f.Ticker != null && f.Ticker.ToLower() == exact)
                || f.Slug.ToLower() == exact
            );
        var aliasSeriesId = FundSeriesSearchAliases.ResolveSeriesId(query);
        var verifiedAlias =
            aliasSeriesId == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(f => f.SeriesId == aliasSeriesId);
        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            matches,
            anyTokenMatches
        );
    }
}

internal static class FundSeriesSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> SeriesIds = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        // These share classes belong to the same SEC series. N-PORT identifies the
        // series, not every class ticker, so the directory row itself has no ticker.
        ["voo"] = "S000002839",
        ["vfiax"] = "S000002839",
        ["vanguard 500"] = "S000002839",
        ["vti"] = "S000002848",
        ["vtsax"] = "S000002848",
        ["vanguard total stock market"] = "S000002848",
    };

    public static string ResolveSeriesId(string query) =>
        SeriesIds.GetValueOrDefault(SearchTerms.Normalize(query));
}
