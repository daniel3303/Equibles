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

public class MarketController : BaseController
{
    private readonly CboePutCallRatioRepository _putCallRepository;
    private readonly CboeVixDailyRepository _vixRepository;

    public MarketController(
        CboePutCallRatioRepository putCallRepository,
        CboeVixDailyRepository vixRepository,
        ILogger<MarketController> logger
    )
        : base(logger)
    {
        _putCallRepository = putCallRepository;
        _vixRepository = vixRepository;
    }

    [HttpGet("~/Market")]
    public async Task<IActionResult> Index()
    {
        var latestRatios = await _putCallRepository.GetLatestPerType().ToListAsync();

        var putCallSummaries = Enum.GetValues<CboePutCallRatioType>()
            .Select(type =>
            {
                var latest = latestRatios.FirstOrDefault(r => r.RatioType == type);
                return new PutCallRatioSummary
                {
                    Type = type,
                    DisplayName = type.NameForHumans(),
                    LatestRatio = latest?.PutCallRatio,
                    LatestCallVolume = latest?.CallVolume,
                    LatestPutVolume = latest?.PutVolume,
                    LatestDate = latest?.Date,
                };
            })
            .ToList();

        var latestVix = await _vixRepository
            .GetAll()
            .OrderByDescending(v => v.Date)
            .Take(2)
            .ToListAsync();

        var oneYearAgo = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var vixRange = await _vixRepository
            .GetByDateRange(oneYearAgo, DateOnly.FromDateTime(DateTime.UtcNow))
            .Select(v => v.Close)
            .ToListAsync();

        var vixSummary = new VixSummary
        {
            LatestClose = latestVix.Count > 0 ? latestVix[0].Close : null,
            PreviousClose = latestVix.Count > 1 ? latestVix[1].Close : null,
            LatestDate = latestVix.Count > 0 ? latestVix[0].Date : null,
            High52Week = vixRange.Count > 0 ? vixRange.Max() : null,
            Low52Week = vixRange.Count > 0 ? vixRange.Min() : null,
        };

        return View(
            new MarketIndexViewModel { PutCallRatios = putCallSummaries, Vix = vixSummary }
        );
    }

    [HttpGet("~/Market/PutCallRatio/{type}")]
    public async Task<IActionResult> PutCallRatio(string type)
    {
        // Enum.TryParse accepts any numeric string in range (e.g. "999" -> an
        // undefined enum), so pair it with Enum.IsDefined to reject values that
        // map to no named member.
        if (
            !Enum.TryParse<CboePutCallRatioType>(type, true, out var ratioType)
            || !Enum.IsDefined(ratioType)
        )
            return NotFound();

        var records = await _putCallRepository
            .GetByType(ratioType)
            .OrderByDescending(r => r.Date)
            .Select(r => new PutCallRatioItem
            {
                Date = r.Date,
                CallVolume = r.CallVolume,
                PutVolume = r.PutVolume,
                TotalVolume = r.TotalVolume,
                PutCallRatio = r.PutCallRatio,
            })
            .ToListAsync();

        var viewModel = new PutCallRatioViewModel
        {
            Type = ratioType,
            DisplayName = ratioType.NameForHumans(),
            Records = records,
        };

        var values = records
            .Where(r => r.PutCallRatio.HasValue)
            .Select(r => (double)r.PutCallRatio.Value)
            .ToArray();
        if (values.Length > 0)
        {
            var s = ComputeStats(values, decimals: 4);
            viewModel.Mean = s.Mean;
            viewModel.Median = s.Median;
            viewModel.Min = s.Min;
            viewModel.Max = s.Max;
            viewModel.StdDev = s.StdDev;
            viewModel.LatestRatio = records.FirstOrDefault()?.PutCallRatio;
            if (records.Count > 1)
                viewModel.PreviousRatio = records[1].PutCallRatio;
        }

        ViewData["Title"] = $"{viewModel.DisplayName} Put/Call Ratio";
        ViewData["Description"] =
            $"CBOE {viewModel.DisplayName} put/call ratio history and statistics.";
        return View(viewModel);
    }

    [HttpGet("~/Market/Vix")]
    public async Task<IActionResult> Vix()
    {
        var records = await _vixRepository
            .GetAll()
            .OrderByDescending(v => v.Date)
            .Select(v => new VixDailyItem
            {
                Date = v.Date,
                Open = v.Open,
                High = v.High,
                Low = v.Low,
                Close = v.Close,
            })
            .ToListAsync();

        var viewModel = new VixViewModel { Records = records };

        var values = records.Select(r => (double)r.Close).ToArray();
        if (values.Length > 0)
        {
            var s = ComputeStats(values, decimals: 2);
            viewModel.Mean = s.Mean;
            viewModel.Median = s.Median;
            viewModel.Min = s.Min;
            viewModel.Max = s.Max;
            viewModel.StdDev = s.StdDev;
            viewModel.LatestClose = records.FirstOrDefault()?.Close;
            if (records.Count > 1)
                viewModel.PreviousClose = records[1].Close;

            var chronological = records.OrderBy(r => r.Date).Select(r => (double)r.Close).ToArray();
            viewModel.Sma20 = chronological.ComputeSma(20, 2);
            viewModel.Sma50 = chronological.ComputeSma(50, 2);
        }

        ViewData["Title"] = "VIX — Volatility Index";
        ViewData["Description"] =
            "CBOE Volatility Index (VIX) daily history with chart and statistics.";
        return View(viewModel);
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
}
