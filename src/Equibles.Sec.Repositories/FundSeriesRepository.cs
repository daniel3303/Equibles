using Equibles.CommonStocks.Data.Helpers;
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

    /// <summary>
    /// Resolves only a canonical profile id, SEC series id, stored series ticker, or verified
    /// share-class alias. Unlike <see cref="Search"/>, this never treats a partial name match as
    /// identity, so read tools can share the directory's authoritative resolution tiers without
    /// silently choosing one row from an ambiguous discovery result.
    /// </summary>
    public IQueryable<FundSeries> ResolveIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return GetAll().Where(_ => false);

        var exact = identifier.Trim().ToLowerInvariant();
        var exactIdentifier = GetAll()
            .Where(f =>
                f.SeriesId.ToLower() == exact
                || (f.Ticker != null && f.Ticker.ToLower() == exact)
                || f.Slug.ToLower() == exact
            );
        var aliasSeriesId = FundSeriesSearchAliases.ResolveSeriesId(identifier);
        var verifiedAlias =
            aliasSeriesId == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(f => f.SeriesId == aliasSeriesId);
        var empty = GetAll().Where(_ => false);

        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            empty,
            empty
        );
    }

    /// <summary>
    /// Resolves only an exact stored series ticker or a verified SEC share-class ticker. This is
    /// the listing-to-series boundary for ETF surfaces; unlike ResolveIdentifier it never accepts
    /// a profile slug, series id, or descriptive search alias.
    /// </summary>
    public IQueryable<FundSeries> ResolveListedClassTicker(string ticker)
    {
        var normalized = TickerNormalizer.NormalizeDashListed(ticker);
        if (normalized == null)
            return GetAll().Where(_ => false);

        var exactTicker = GetAll().Where(f => f.ClassTickers.Contains(normalized));
        var uniqueExactTicker = exactTicker.Where(_ => exactTicker.Count() == 1);
        var empty = GetAll().Where(_ => false);
        return SearchTerms.WithExclusiveResolutionTiers(uniqueExactTicker, empty, empty, empty);
    }
}

internal static class FundSeriesSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> ClassTickerSeriesIds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // These share classes belong to the same SEC series. N-PORT identifies the
            // series, not every class ticker, so the directory row itself has no ticker.
            ["voo"] = "S000002839",
            ["vfiax"] = "S000002839",
            ["vti"] = "S000002848",
            ["vtsax"] = "S000002848",
        };

    private static readonly IReadOnlyDictionary<string, string> NameSeriesIds = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["vanguard 500"] = "S000002839",
        ["vanguard total stock market"] = "S000002848",
    };

    public static string ResolveSeriesId(string query) =>
        ResolveClassTickerSeriesId(query)
        ?? NameSeriesIds.GetValueOrDefault(SearchTerms.Normalize(query));

    public static string ResolveClassTickerSeriesId(string ticker) =>
        ClassTickerSeriesIds.GetValueOrDefault(SearchTerms.Normalize(ticker));
}
