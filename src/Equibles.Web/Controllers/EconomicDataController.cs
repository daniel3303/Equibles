using Equibles.Core.Extensions;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.EconomicData;
using MathNet.Numerics.Statistics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class EconomicDataController : BaseController
{
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;

    public EconomicDataController(
        FredSeriesRepository seriesRepository,
        FredObservationRepository observationRepository,
        ILogger<EconomicDataController> logger
    )
        : base(logger)
    {
        _seriesRepository = seriesRepository;
        _observationRepository = observationRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var allSeries = await _seriesRepository
            .GetAll()
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Title)
            .ToListAsync();

        var latestBySeriesId = await _observationRepository
            .GetLatestPerSeries()
            .ToDictionaryAsync(o => o.FredSeriesId, o => o);

        var categories = allSeries
            .GroupBy(s => s.Category)
            .Select(g => new EconomyCategoryGroup
            {
                Category = g.Key,
                DisplayName = g.Key.NameForHumans(),
                Series = g.Select(s =>
                    {
                        latestBySeriesId.TryGetValue(s.Id, out var latest);
                        return new EconomySeriesItem
                        {
                            SeriesId = s.SeriesId,
                            Title = s.Title,
                            Units = s.Units,
                            Frequency = ExpandFrequency(s.Frequency),
                            LatestValue = latest?.Value,
                            LatestDate = latest?.Date,
                        };
                    })
                    .ToList(),
            })
            .ToList();

        return View(new EconomyIndexViewModel { Categories = categories });
    }

    [HttpGet("~/EconomicData/{seriesId}")]
    public async Task<IActionResult> Show(string seriesId)
    {
        if (string.IsNullOrWhiteSpace(seriesId))
            return NotFound();

        var series = await _seriesRepository
            .GetBySeriesId(seriesId.ToUpperInvariant())
            .FirstOrDefaultAsync();
        if (series == null)
            return NotFound();

        var observations = await _observationRepository
            .GetBySeries(series)
            .Where(o => o.Value != null)
            .OrderByDescending(o => o.Date)
            .Select(o => new ObservationItem { Date = o.Date, Value = o.Value })
            .ToListAsync();

        var viewModel = new EconomySeriesViewModel
        {
            SeriesId = series.SeriesId,
            Title = series.Title,
            Category = series.Category,
            CategoryDisplayName = series.Category.NameForHumans(),
            Frequency = ExpandFrequency(series.Frequency),
            Units = series.Units,
            SeasonalAdjustment = series.SeasonalAdjustment,
            Observations = observations,
        };

        // Chronological order is needed for SMA; stats are order-invariant so the
        // same array drives both.
        var chronological = observations
            .Where(o => o.Value.HasValue)
            .OrderBy(o => o.Date)
            .Select(o => (double)o.Value.Value)
            .ToArray();
        if (chronological.Length > 0)
        {
            var s = ComputeStats(chronological, decimals: 4);
            viewModel.Mean = s.Mean;
            viewModel.Min = s.Min;
            viewModel.Max = s.Max;
            viewModel.Median = s.Median;
            viewModel.StdDev = s.StdDev;
            viewModel.LatestValue = observations[0].Value; // observations are desc by date
            if (observations.Count > 1)
                viewModel.PreviousValue = observations[1].Value;

            viewModel.Sma20 = ComputeSma(chronological, 20);
            viewModel.Sma50 = ComputeSma(chronological, 50);
        }

        ViewData["Title"] = $"{series.SeriesId} — {series.Title}";
        ViewData["Description"] =
            $"{series.Title} ({series.SeriesId}) — {series.Units}. FRED economic data.";
        return View(viewModel);
    }

    private static List<decimal?> ComputeSma(double[] values, int period)
    {
        var sma = values.MovingAverage(period);
        return sma.Select((v, i) => i < period - 1 ? (decimal?)null : (decimal?)Math.Round(v, 4))
            .ToList();
    }

    private static StatsSummary ComputeStats(double[] values, int decimals)
    {
        var stats = new DescriptiveStatistics(values);
        return new StatsSummary(
            Mean: stats.Mean.SafeRound(decimals),
            Median: values.Median().SafeRound(decimals),
            Min: stats.Minimum.SafeRound(decimals),
            Max: stats.Maximum.SafeRound(decimals),
            StdDev: stats.StandardDeviation.SafeRound(decimals)
        );
    }

    private readonly record struct StatsSummary(
        decimal? Mean,
        decimal? Median,
        decimal? Min,
        decimal? Max,
        decimal? StdDev
    );

    private static string ExpandFrequency(string frequency) =>
        frequency?.Trim().ToUpperInvariant() switch
        {
            "D" => "Daily",
            "W" => "Weekly",
            "BW" => "Biweekly",
            "M" => "Monthly",
            "Q" => "Quarterly",
            "SA" => "Semiannual",
            "A" => "Annual",
            _ => frequency,
        };
}
