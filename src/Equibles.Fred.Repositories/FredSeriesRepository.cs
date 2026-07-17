using Equibles.Data;
using Equibles.Fred.Data.Models;

namespace Equibles.Fred.Repositories;

public class FredSeriesRepository : BaseRepository<FredSeries>
{
    public FredSeriesRepository(EquiblesFinancialDbContext dbContext)
        : base(dbContext) { }

    public IQueryable<FredSeries> GetBySeriesId(string seriesId)
    {
        return GetAll().Where(s => s.SeriesId == seriesId);
    }

    public IQueryable<FredSeries> GetByCategory(FredSeriesCategory category)
    {
        return GetAll().Where(s => s.Category == category);
    }

    public IQueryable<FredSeries> Search(string query)
    {
        var lower = (query ?? string.Empty).Trim().ToLower();
        if (lower.Length == 0)
            return GetAll();

        // Category-name matches widen recall: "inflation" must return the whole
        // Inflation category (CPI, PCE, PPI, breakevens), not only the titles that
        // happen to contain the word. Matched categories are computed client-side
        // (the universe is a fixed enum) so the EF query stays a translatable
        // Contains over SeriesId/Title plus a constant category list.
        var matchedCategories = MatchCategories(lower);
        return GetAll()
            .Where(s =>
                s.SeriesId.ToLower().Contains(lower)
                || s.Title.ToLower().Contains(lower)
                || matchedCategories.Contains(s.Category)
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
