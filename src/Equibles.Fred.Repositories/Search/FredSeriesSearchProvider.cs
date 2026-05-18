using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Repositories.Search;

/// <summary>Economic-indicator group. Wraps the existing FRED series id/title search.</summary>
public class FredSeriesSearchProvider : ISearchProvider
{
    private readonly FredSeriesRepository _fredSeriesRepository;

    public FredSeriesSearchProvider(FredSeriesRepository fredSeriesRepository)
    {
        _fredSeriesRepository = fredSeriesRepository;
    }

    public string Category => "Economic Indicators";

    public int Order => 20;

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var series = await _fredSeriesRepository
            .Search(request.Query)
            .OrderBy(s => s.Title)
            .Take(request.MaxPerProvider)
            .Select(s => new
            {
                s.SeriesId,
                s.Title,
                s.Units,
            })
            .ToListAsync(cancellationToken);

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = series
                .Select(s => new SearchHit
                {
                    Title = s.Title,
                    Subtitle = string.IsNullOrWhiteSpace(s.Units)
                        ? s.SeriesId
                        : $"{s.SeriesId} · {s.Units}",
                    Kind = "EconomicSeries",
                    RouteValues = { ["seriesId"] = s.SeriesId },
                })
                .ToList(),
        };
    }
}
