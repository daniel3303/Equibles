using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// Computes a filer's fund score: the hypothetical buy-and-hold return of its reported 13F
/// portfolio over a rolling window, against a benchmark. Reuses
/// <see cref="HoldingsBacktestCalculator"/> (same look-ahead-safe rebalancing the on-demand
/// institution backtest uses) and persists the result as a <see cref="FundScore"/>.
/// <para>
/// The snapshot-building and price forward-fill mirror the on-demand backtest in the web
/// portal's HoldingsBacktestService; kept self-contained here so the scoring worker has no
/// dependency on the web host.
/// </para>
/// </summary>
[Service]
public class FundScoringManager
{
    public const string DefaultBenchmark = "SPY";
    public const int DefaultWindowYears = 3;

    // Annualised compounding can saturate to decimal.MaxValue on degenerate inputs; numeric(18,4)
    // tops out far below that, so anything past this magnitude isn't a storable (or meaningful) score.
    private const decimal MaxStorableMagnitude = 9_999_999_999_999m;

    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly DailyStockPriceRepository _priceRepository;
    private readonly FundScoreRepository _fundScoreRepository;

    public FundScoringManager(
        InstitutionalHoldingRepository holdingRepository,
        CommonStockRepository stockRepository,
        DailyStockPriceRepository priceRepository,
        FundScoreRepository fundScoreRepository
    )
    {
        _holdingRepository = holdingRepository;
        _stockRepository = stockRepository;
        _priceRepository = priceRepository;
        _fundScoreRepository = fundScoreRepository;
    }

    /// <summary>
    /// Computes and persists the rolling-window fund score for one filer, measured against
    /// <paramref name="benchmarkTicker"/>. Returns the saved score, or null when there isn't
    /// enough data to simulate (unknown benchmark, no 13F snapshots in range, missing benchmark
    /// prices, or a non-finite result). Recomputes in place — an existing score for the same
    /// (holder, window, benchmark) is updated rather than duplicated.
    /// </summary>
    public async Task<FundScore> ScoreHolder(
        InstitutionalHolder holder,
        DateOnly asOf,
        int windowYears = DefaultWindowYears,
        string benchmarkTicker = DefaultBenchmark
    )
    {
        benchmarkTicker = benchmarkTicker.Trim().ToUpperInvariant();

        var benchmarkStock = await _stockRepository.GetByTicker(benchmarkTicker);
        if (benchmarkStock == null)
            return null;

        var result = await RunBacktest(holder, asOf, windowYears, benchmarkStock);
        if (result == null || result.Points.Count == 0 || !IsStorable(result))
            return null;

        return await Upsert(holder, windowYears, benchmarkTicker, result);
    }

    private async Task<BacktestResult> RunBacktest(
        InstitutionalHolder holder,
        DateOnly asOf,
        int windowYears,
        CommonStock benchmarkStock
    )
    {
        var reportDates = await _holdingRepository.GetReportDatesByHolder(holder).ToListAsync();
        if (reportDates.Count == 0)
            return null;
        // GetReportDatesByHolder returns latest-first; SelectRelevantSnapshotDates needs
        // earliest-first so the "last snapshot before the window" lands on the most recent one.
        reportDates.Sort();

        var to = asOf;
        var from = asOf.Year > windowYears ? asOf.AddYears(-windowYears) : DateOnly.MinValue;

        var relevant = SelectRelevantSnapshotDates(reportDates, from, to);
        if (relevant.Count == 0)
            return null;

        var holdings = await _holdingRepository
            .GetHistoryByHolder(holder)
            .Where(h => relevant.Contains(h.ReportDate))
            .Select(h => new HoldingRow(
                h.ReportDate,
                h.CommonStockId,
                h.Shares,
                h.Value,
                h.OptionType
            ))
            .ToListAsync();

        var snapshots = BuildSnapshots(holdings);

        var priceWindowFrom =
            from > DateOnly.MinValue.AddDays(BacktestPriceLoader.PriceLookbackDays)
                ? from.AddDays(-BacktestPriceLoader.PriceLookbackDays)
                : DateOnly.MinValue;

        var stockIds = holdings
            .Where(h => h.OptionType == null && h.Value > 0)
            .Select(h => h.CommonStockId)
            .Distinct()
            .ToList();

        var pricesByStock = (
            stockIds.Count == 0
                ? []
                : await BacktestPriceLoader.LoadPrices(
                    _priceRepository.GetByStocks(stockIds, priceWindowFrom, to)
                )
        )
            .GroupBy(p => p.StockId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var benchmarkSeries = (
            await BacktestPriceLoader.LoadPrices(
                _priceRepository.GetByStock(benchmarkStock, priceWindowFrom, to)
            )
        ).ToArray();
        if (benchmarkSeries.Length == 0)
            return null;

        return HoldingsBacktestCalculator.Calculate(
            snapshots,
            from,
            to,
            priceOf: (stockId, date) =>
                BacktestPriceLoader.ForwardFill(pricesByStock, stockId, date),
            benchmarkPriceOf: date => BacktestPriceLoader.ForwardFill(benchmarkSeries, date)
        );
    }

    private async Task<FundScore> Upsert(
        InstitutionalHolder holder,
        int windowYears,
        string benchmarkTicker,
        BacktestResult result
    )
    {
        var score = await _fundScoreRepository.GetByHolder(holder, windowYears, benchmarkTicker);
        if (score == null)
        {
            score = new FundScore
            {
                InstitutionalHolderId = holder.Id,
                WindowYears = windowYears,
                BenchmarkTicker = benchmarkTicker,
            };
            _fundScoreRepository.Add(score);
        }

        score.WindowStart = result.StartDate;
        score.WindowEnd = result.EndDate;
        score.PortfolioTotalReturnPercent = result.PortfolioSummary.TotalReturnPercent;
        score.PortfolioCagrPercent = result.PortfolioSummary.CagrPercent;
        score.BenchmarkTotalReturnPercent = result.BenchmarkSummary.TotalReturnPercent;
        score.BenchmarkCagrPercent = result.BenchmarkSummary.CagrPercent;
        score.AlphaPercent =
            result.PortfolioSummary.CagrPercent - result.BenchmarkSummary.CagrPercent;
        score.MaxDrawdownPercent = result.PortfolioSummary.MaxDrawdownPercent;
        score.CreationTime = DateTime.UtcNow;

        await _fundScoreRepository.SaveChanges();
        return score;
    }

    // All snapshots whose rebalance date falls in [from, to], plus the latest one whose rebalance
    // precedes `from` so the simulation can open with an already-held portfolio.
    private static List<DateOnly> SelectRelevantSnapshotDates(
        IReadOnlyList<DateOnly> reportDates,
        DateOnly from,
        DateOnly to
    )
    {
        var relevant = new List<DateOnly>();
        DateOnly? lastBeforeWindow = null;
        foreach (var date in reportDates)
        {
            var rebalance = date.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
            if (rebalance < from)
                lastBeforeWindow = date;
            else if (rebalance <= to)
                relevant.Add(date);
        }
        if (lastBeforeWindow.HasValue && !relevant.Contains(lastBeforeWindow.Value))
            relevant.Insert(0, lastBeforeWindow.Value);
        return relevant;
    }

    private static List<BacktestQuarterSnapshot> BuildSnapshots(
        IReadOnlyList<HoldingRow> holdings
    ) =>
        holdings
            .GroupBy(h => h.ReportDate)
            .OrderBy(g => g.Key)
            .Select(g => new BacktestQuarterSnapshot
            {
                ReportDate = g.Key,
                Positions = g.Select(h => new BacktestPosition
                    {
                        CommonStockId = h.CommonStockId,
                        Shares = h.Shares,
                        Value = h.Value,
                        IsOption = h.OptionType != null,
                    })
                    .ToList(),
            })
            .ToList();

    private static bool IsStorable(BacktestResult result) =>
        InRange(result.PortfolioSummary.TotalReturnPercent)
        && InRange(result.PortfolioSummary.CagrPercent)
        && InRange(result.PortfolioSummary.MaxDrawdownPercent)
        && InRange(result.BenchmarkSummary.TotalReturnPercent)
        && InRange(result.BenchmarkSummary.CagrPercent);

    private static bool InRange(decimal value) => Math.Abs(value) < MaxStorableMagnitude;

    private readonly record struct HoldingRow(
        DateOnly ReportDate,
        Guid CommonStockId,
        long Shares,
        long Value,
        OptionType? OptionType
    );
}
