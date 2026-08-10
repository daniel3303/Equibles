using Equibles.Data;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredSeriesRepository : BaseRepository<FredSeries>
{
    public FredSeriesRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FredSeries> GetBySeriesId(string seriesId)
    {
        var canonical = seriesId?.Trim().ToUpperInvariant();
        var alias = FredSeriesSearchAliases.Resolve(seriesId);
        return GetAll()
            .Where(s => s.SeriesId == canonical || (alias != null && s.SeriesId == alias))
            .OrderBy(s => s.SeriesId == canonical ? 0 : 1);
    }

    public IQueryable<FredSeries> GetByCategory(FredSeriesCategory category)
    {
        return GetAll().Where(s => s.Category == category);
    }

    public IQueryable<FredSeries> Search(string query)
    {
        var tokens = SearchTerms.Tokenize(query);
        if (tokens.Count == 0)
            return string.IsNullOrWhiteSpace(query) ? GetAll() : GetAll().Where(_ => false);

        // Every natural-language token may occur anywhere in the id/title/category;
        // punctuation and word order no longer have to mirror the stored title. If no row
        // satisfies every token or an exact alias, the composable query broadens to any token.
        // Category matches stay client-derived constants so each loop is translatable.
        var matches = GetAll();
        var anyTokenMatches = GetAll().Where(_ => false);
        foreach (var token in tokens)
        {
            var matchedCategories = MatchCategories(token);
            matches = matches.Where(s =>
                s.SeriesId.ToLower().Contains(token)
                || s.Title.ToLower().Contains(token)
                || matchedCategories.Contains(s.Category)
            );
            anyTokenMatches = anyTokenMatches.Concat(
                GetAll()
                    .Where(s =>
                        s.SeriesId.ToLower().Contains(token)
                        || s.Title.ToLower().Contains(token)
                        || matchedCategories.Contains(s.Category)
                    )
            );
        }

        // Exact stored ids and the small verified vocabulary are authoritative resolution
        // tiers. Ordinary token matches apply only when neither tier resolves a tracked row.
        var canonical = query.Trim().ToUpperInvariant();
        var exactIdentifier = GetAll().Where(s => s.SeriesId == canonical);
        var aliasId = FredSeriesSearchAliases.Resolve(query);
        var verifiedAlias =
            aliasId == null
                ? GetAll().Where(_ => false)
                : GetAll().Where(s => s.SeriesId == aliasId);
        return SearchTerms.WithExclusiveResolutionTiers(
            exactIdentifier,
            verifiedAlias,
            matches,
            anyTokenMatches
        );
    }

    // A category matches when its name contains the query ("inflation" -> Inflation,
    // "rates" -> InterestRates/ExchangeRates) or the query contains the name
    // ("unemployment" contains "employment" -> Employment). Both sides are reduced
    // to lowercase alphanumerics so "interest rates" still matches InterestRates.
    private static List<FredSeriesCategory> MatchCategories(string lowerQuery)
    {
        var normalizedQuery = NormalizeForCategoryMatch(lowerQuery);
        if (normalizedQuery.Length == 0)
            return [];

        return Enum.GetValues<FredSeriesCategory>()
            .Where(c =>
            {
                var name = NormalizeForCategoryMatch(c.ToString().ToLower());
                return name.Contains(normalizedQuery) || normalizedQuery.Contains(name);
            })
            .ToList();
    }

    private static string NormalizeForCategoryMatch(string text) =>
        new(text.Where(char.IsLetterOrDigit).ToArray());
}

internal static class FredSeriesSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string> SeriesIds = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["fed funds rate"] = "FEDFUNDS",
        ["federal funds rate"] = "FEDFUNDS",
        ["jobless claims"] = "ICSA",
        ["initial jobless claims"] = "ICSA",
        ["payrolls"] = "PAYEMS",
        ["nonfarm payrolls"] = "PAYEMS",
        ["non farm payrolls"] = "PAYEMS",
        ["yield curve"] = "T10Y2Y",
        ["treasury yield curve"] = "T10Y2Y",
        ["core cpi"] = "CPILFESL",
    };

    public static string Resolve(string query) =>
        SeriesIds.GetValueOrDefault(SearchTerms.Normalize(query));
}
