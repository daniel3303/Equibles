using Equibles.Fred.Data.Models;
using Equibles.Search.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Fred.Repositories.Search;

/// <summary>Economic-indicator group. Wraps the existing FRED series id/title search.</summary>
public class FredSeriesSearchProvider : QueryableSearchProvider<FredSeries>
{
    private readonly FredSeriesRepository _fredSeriesRepository;

    public FredSeriesSearchProvider(FredSeriesRepository fredSeriesRepository)
    {
        _fredSeriesRepository = fredSeriesRepository;
    }

    public override string Category => "Economic Indicators";

    public override int Order => 20;

    protected override IQueryable<FredSeries> Filter(SearchRequest request) =>
        _fredSeriesRepository.Search(request.Query).OrderBy(series => series.Title);

    protected override Task<List<FredSeries>> Materialize(
        IQueryable<FredSeries> query,
        CancellationToken cancellationToken
    ) => query.ToListAsync(cancellationToken);

    protected override SearchHit Project(FredSeries series) =>
        new()
        {
            Title = series.Title,
            Subtitle = string.IsNullOrWhiteSpace(series.Units)
                ? series.SeriesId
                : $"{series.SeriesId} · {series.Units}",
            Kind = "EconomicSeries",
            RouteValues = { ["seriesId"] = series.SeriesId },
        };
}
