using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Core.Extensions;
using Equibles.Web.Controllers.Abstract;
using Equibles.Web.Extensions;
using Equibles.Web.ViewModels.Market;
using MathNet.Numerics.Statistics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Controllers;

public class MarketController : BaseController {
    private readonly CboePutCallRatioRepository _putCallRepository;
    private readonly CboeVixDailyRepository _vixRepository;

    public MarketController(
        CboePutCallRatioRepository putCallRepository,
        CboeVixDailyRepository vixRepository,
        ILogger<MarketController> logger
    ) : base(logger) {
        _putCallRepository = putCallRepository;
        _vixRepository = vixRepository;
    }

    [HttpGet("~/Market")]
    public async Task<IActionResult> Index() {
        var latestRatios = await _putCallRepository.GetLatestPerType().ToListAsync();

        var putCallSummaries = Enum.GetValues<CboePutCallRatioType>()
            .Select(type => {
                var latest = latestRatios.FirstOrDefault(r => r.RatioType == type);
                return new PutCallRatioSummary {
                    Type = type,
                    DisplayName = type.NameForHumans(),
                    LatestRatio = latest?.PutCallRatio,
                    LatestCallVolume = latest?.CallVolume,
                    LatestPutVolume = latest?.PutVolume,
                    LatestDate = latest?.Date
                };
            })
            .ToList();

        // VIX summary
        var latestVix = await _vixRepository.GetAll()
            .OrderByDescending(v => v.Date)
            .Take(2)
            .ToListAsync();

        var oneYearAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var vixRange = await _vixRepository.GetByDateRange(oneYearAgo, DateOnly.FromDateTime(DateTime.UtcNow))
            .Select(v => v.Close)
            .ToListAsync();

        var vixSummary = new VixSummary {
            LatestClose = latestVix.Count > 0 ? latestVix[0].Close : null,
            PreviousClose = latestVix.Count > 1 ? latestVix[1].Close : null,
            LatestDate = latestVix.Count > 0 ? latestVix[0].Date : null,
            High52Week = vixRange.Count > 0 ? vixRange.Max() : null,
            Low52Week = vixRange.Count > 0 ? vixRange.Min() : null
        };

        return View(new MarketIndexViewModel { PutCallRatios = putCallSummaries, Vix = vixSummary });
    }

    [HttpGet("~/Market/PutCallRatio/{type}")]
    public async Task<IActionResult> PutCallRatio(string type) {
        if (!Enum.TryParse<CboePutCallRatioType>(type, true, out var ratioType)) return NotFound();

        var records = await _putCallRepository.GetByType(ratioType)
            .OrderByDescending(r => r.Date)
            .Select(r => new PutCallRatioItem {
                Date = r.Date,
                CallVolume = r.CallVolume,
                PutVolume = r.PutVolume,
                TotalVolume = r.TotalVolume,
                PutCallRatio = r.PutCallRatio
            })
            .ToListAsync();

        var viewModel = new PutCallRatioViewModel {
            Type = ratioType,
            DisplayName = ratioType.NameForHumans(),
            Records = records
        };

        // Compute statistics
        var values = records.Where(r => r.PutCallRatio.HasValue).Select(r => (double)r.PutCallRatio.Value).ToArray();
        if (values.Length > 0) {
            var stats = new DescriptiveStatistics(values);
            viewModel.Mean = stats.Mean.SafeRound(4);
            viewModel.Median = values.Median().SafeRound(4);
            viewModel.Min = stats.Minimum.SafeRound(4);
            viewModel.Max = stats.Maximum.SafeRound(4);
            viewModel.StdDev = stats.StandardDeviation.SafeRound(4);
            viewModel.LatestRatio = records.FirstOrDefault()?.PutCallRatio;
            if (records.Count > 1) viewModel.PreviousRatio = records[1].PutCallRatio;
        }

        ViewData["Title"] = $"{viewModel.DisplayName} Put/Call Ratio";
        ViewData["Description"] = $"CBOE {viewModel.DisplayName} put/call ratio history and statistics.";
        return View(viewModel);
    }

    [HttpGet("~/Market/Vix")]
    public async Task<IActionResult> Vix() {
        var records = await _vixRepository.GetAll()
            .OrderByDescending(v => v.Date)
            .Select(v => new VixDailyItem {
                Date = v.Date,
                Open = v.Open,
                High = v.High,
                Low = v.Low,
                Close = v.Close
            })
            .ToListAsync();

        var viewModel = new VixViewModel { Records = records };

        var values = records.Select(r => (double)r.Close).ToArray();
        if (values.Length > 0) {
            var stats = new DescriptiveStatistics(values);
            viewModel.Mean = stats.Mean.SafeRound(2);
            viewModel.Median = values.Median().SafeRound(2);
            viewModel.Min = stats.Minimum.SafeRound(2);
            viewModel.Max = stats.Maximum.SafeRound(2);
            viewModel.StdDev = stats.StandardDeviation.SafeRound(2);
            viewModel.LatestClose = records.FirstOrDefault()?.Close;
            if (records.Count > 1) viewModel.PreviousClose = records[1].Close;

            // Moving averages (chronological order)
            var chronological = records.OrderBy(r => r.Date).Select(r => (double)r.Close).ToArray();
            viewModel.Sma20 = chronological.ComputeSma(20, 2);
            viewModel.Sma50 = chronological.ComputeSma(50, 2);
        }

        ViewData["Title"] = "VIX — Volatility Index";
        ViewData["Description"] = "CBOE Volatility Index (VIX) daily history with chart and statistics.";
        return View(viewModel);
    }
}
