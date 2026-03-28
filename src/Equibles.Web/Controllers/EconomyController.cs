using Equibles.Core.Extensions;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.ViewModels.Economy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class EconomyController : BaseController {
    private readonly FredSeriesRepository _seriesRepository;
    private readonly FredObservationRepository _observationRepository;

    public EconomyController(
        FredSeriesRepository seriesRepository,
        FredObservationRepository observationRepository,
        ILogger<EconomyController> logger
    ) : base(logger) {
        _seriesRepository = seriesRepository;
        _observationRepository = observationRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index() {
        ViewData["Title"] = "Economic Indicators";
        ViewData["Description"] = "Browse FRED economic indicators — interest rates, inflation, employment, GDP, and more.";

        var allSeries = await _seriesRepository.GetAll()
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Title)
            .ToListAsync();

        var latestBySeriesId = await _observationRepository.GetLatestPerSeries()
            .ToDictionaryAsync(o => o.FredSeriesId, o => o);

        var categories = allSeries
            .GroupBy(s => s.Category)
            .Select(g => new EconomyCategoryGroup {
                Category = g.Key,
                DisplayName = g.Key.NameForHumans(),
                Series = g.Select(s => {
                    latestBySeriesId.TryGetValue(s.Id, out var latest);
                    return new EconomySeriesItem {
                        SeriesId = s.SeriesId,
                        Title = s.Title,
                        Units = s.Units,
                        Frequency = s.Frequency,
                        LatestValue = latest?.Value,
                        LatestDate = latest?.Date
                    };
                }).ToList()
            })
            .ToList();

        return View(new EconomyIndexViewModel { Categories = categories });
    }

    [HttpGet("~/Economy/{seriesId}")]
    public async Task<IActionResult> Show(string seriesId) {
        var series = await _seriesRepository.GetBySeriesId(seriesId).FirstOrDefaultAsync();
        if (series == null) return NotFound();

        var observations = await _observationRepository.GetBySeries(series)
            .Where(o => o.Value != null)
            .OrderByDescending(o => o.Date)
            .Take(500)
            .Select(o => new ObservationItem {
                Date = o.Date,
                Value = o.Value
            })
            .ToListAsync();

        var viewModel = new EconomySeriesViewModel {
            SeriesId = series.SeriesId,
            Title = series.Title,
            Category = series.Category,
            CategoryDisplayName = series.Category.NameForHumans(),
            Frequency = series.Frequency,
            Units = series.Units,
            SeasonalAdjustment = series.SeasonalAdjustment,
            Observations = observations
        };

        ViewData["Title"] = $"{series.SeriesId} — {series.Title}";
        ViewData["Description"] = $"{series.Title} ({series.SeriesId}) — {series.Units}. FRED economic data.";
        return View(viewModel);
    }
}
