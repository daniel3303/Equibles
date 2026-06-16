using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.BusinessLogic;

/// <summary>
/// On-demand "clone performance" backtest for a single institutional filer: reconstructs the
/// filer's reported 13F portfolio each quarter and values it forward against a benchmark, so a
/// caller can answer "how would cloning fund X have performed". Shared by the web profile page
/// and the MCP tool so the on-demand series never disagrees between channels; the look-ahead-safe
/// simulation and price loading are delegated to <see cref="BacktestPriceLoader"/>.
/// </summary>
[Service]
public class HoldingsCloneBacktestProvider
{
    public const string DefaultBenchmark = "SPY";

    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly DailyStockPriceRepository _priceRepository;

    public HoldingsCloneBacktestProvider(
        InstitutionalHolderRepository holderRepository,
        InstitutionalHoldingRepository holdingRepository,
        CommonStockRepository stockRepository,
        DailyStockPriceRepository priceRepository
    )
    {
        _holderRepository = holderRepository;
        _holdingRepository = holdingRepository;
        _stockRepository = stockRepository;
        _priceRepository = priceRepository;
    }

    /// <summary>
    /// Runs the clone backtest for the filer identified by <paramref name="cik"/> over
    /// [<paramref name="from"/>, <paramref name="to"/>] (defaults: earliest snapshot's rebalance
    /// date through today) against <paramref name="benchmark"/> (default SPY). The returned
    /// <see cref="CloneBacktestOutcome"/> carries not-found flags and an empty result with a
    /// <see cref="BacktestResult.Reason"/> when there isn't enough data to simulate.
    /// </summary>
    public async Task<CloneBacktestOutcome> Run(
        string cik,
        DateOnly? from,
        DateOnly? to,
        string benchmark
    )
    {
        var outcome = new CloneBacktestOutcome
        {
            Cik = cik,
            Benchmark = string.IsNullOrWhiteSpace(benchmark)
                ? DefaultBenchmark
                : TickerNormalizer.Normalize(benchmark),
            RequestedFrom = from,
            RequestedTo = to,
        };

        var holder = await _holderRepository.GetByCik(cik);
        if (holder == null)
        {
            outcome.HolderNotFound = true;
            return outcome;
        }
        outcome.HolderName = holder.Name;

        var benchmarkStock = await _stockRepository.GetByTicker(outcome.Benchmark);
        if (benchmarkStock == null)
        {
            outcome.BenchmarkNotFound = true;
            return outcome;
        }
        outcome.BenchmarkName = benchmarkStock.Name;

        var reportDates = await _holdingRepository.Get13FReportDatesByHolder(holder).ToListAsync();
        // Get13FReportDatesByHolder returns latest first; the backtest iterates earliest first.
        // 13D/G event dates are excluded so a single disclosed stake never rotates the whole
        // simulation into one stock at the event date.
        reportDates.Reverse();
        if (reportDates.Count == 0)
        {
            outcome.Result.Reason = "Holder has no 13F snapshots on file.";
            return outcome;
        }

        var resolvedTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var firstRebalance = HoldingsBacktestCalculator.RebalanceDateOf(reportDates[0]);
        var resolvedFrom = from ?? firstRebalance;
        outcome.ResolvedFrom = resolvedFrom;
        outcome.ResolvedTo = resolvedTo;

        var relevant = SelectRelevantSnapshotDates(reportDates, resolvedFrom, resolvedTo);
        if (relevant.Count == 0)
        {
            outcome.Result.Reason =
                "No 13F snapshot's rebalance date falls inside the requested window.";
            return outcome;
        }

        var holdings = await _holdingRepository
            .Get13FHistoryByHolder(holder)
            .Where(h => relevant.Contains(h.ReportDate))
            .Select(h => new BacktestHoldingRow(
                h.ReportDate,
                h.CommonStockId,
                h.Shares,
                h.Value,
                h.OptionType
            ))
            .ToListAsync();

        var snapshots = BuildQuarterSnapshots(holdings);

        var stockIds = holdings
            .Where(h => h.OptionType == null && h.Value > 0)
            .Select(h => h.CommonStockId)
            .Distinct()
            .ToList();

        var result = await BacktestPriceLoader.RunBacktest(
            _priceRepository,
            snapshots,
            stockIds,
            benchmarkStock,
            resolvedFrom,
            resolvedTo
        );
        if (result == null)
        {
            outcome.Result.Reason =
                $"No price data available for benchmark {outcome.Benchmark} in the requested window.";
            return outcome;
        }

        outcome.Result = result;
        return outcome;
    }

    private static List<BacktestQuarterSnapshot> BuildQuarterSnapshots(
        IReadOnlyList<BacktestHoldingRow> holdings
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

    // Select all snapshots whose rebalance date falls in [resolvedFrom, resolvedTo], plus the
    // latest one whose rebalance date precedes resolvedFrom so the simulation can open with an
    // initial portfolio.
    private static List<DateOnly> SelectRelevantSnapshotDates(
        IReadOnlyList<DateOnly> reportDates,
        DateOnly resolvedFrom,
        DateOnly resolvedTo
    )
    {
        var relevant = new List<DateOnly>();
        DateOnly? lastBeforeWindow = null;
        foreach (var date in reportDates)
        {
            var rebalance = HoldingsBacktestCalculator.RebalanceDateOf(date);
            if (rebalance < resolvedFrom)
                lastBeforeWindow = date;
            else if (rebalance <= resolvedTo)
                relevant.Add(date);
        }
        if (lastBeforeWindow.HasValue && !relevant.Contains(lastBeforeWindow.Value))
            relevant.Insert(0, lastBeforeWindow.Value);
        return relevant;
    }

    private readonly record struct BacktestHoldingRow(
        DateOnly ReportDate,
        Guid CommonStockId,
        long Shares,
        long Value,
        OptionType? OptionType
    );
}
