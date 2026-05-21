using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.ViewModels.Profiles;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Web.Services;

[Service]
public class HoldingsBacktestService
{
    public const string DefaultBenchmark = "SPY";

    // Tickers offered in the form's dropdown — kept small so the picker is curated rather
    // than letting users type arbitrary symbols that may not have full price history.
    private static readonly string[] CandidateBenchmarks =
    [
        "SPY",
        "QQQ",
        "IWM",
        "DIA",
        "VTI",
        "VOO",
    ];

    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly DailyStockPriceRepository _priceRepository;

    public HoldingsBacktestService(
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

    public async Task<BacktestViewModel> Execute(
        string cik,
        DateOnly? from,
        DateOnly? to,
        string benchmark
    )
    {
        var viewModel = new BacktestViewModel
        {
            Cik = cik,
            Benchmark = string.IsNullOrWhiteSpace(benchmark)
                ? DefaultBenchmark
                : benchmark.Trim().ToUpperInvariant(),
            RequestedFrom = from,
            RequestedTo = to,
        };
        viewModel.BenchmarkOptions = await LoadBenchmarkOptions();

        var holder = await _holderRepository.GetByCik(cik);
        if (holder == null)
        {
            viewModel.HolderNotFound = true;
            return viewModel;
        }
        viewModel.HolderName = holder.Name;

        var benchmarkStock = await _stockRepository.GetByTicker(viewModel.Benchmark);
        if (benchmarkStock == null)
        {
            viewModel.BenchmarkNotFound = true;
            viewModel.ErrorMessage = $"Benchmark ticker '{viewModel.Benchmark}' is not known.";
            return viewModel;
        }
        viewModel.BenchmarkName = benchmarkStock.Name;

        var reportDates = await _holdingRepository
            .GetHistoryByHolder(holder)
            .Select(h => h.ReportDate)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync();
        if (reportDates.Count == 0)
        {
            viewModel.Result.Reason = "Holder has no 13F snapshots on file.";
            return viewModel;
        }

        var resolvedTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var firstRebalance = reportDates[0].AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
        var resolvedFrom = from ?? firstRebalance;

        var relevant = SelectRelevantSnapshotDates(reportDates, resolvedFrom, resolvedTo);

        if (relevant.Count == 0)
        {
            viewModel.Result.Reason =
                "No 13F snapshot's rebalance date falls inside the requested window.";
            return viewModel;
        }

        var holdings = await _holdingRepository
            .GetHistoryByHolder(holder)
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

        // Forward-fill needs a few days of pre-window prices so day-zero of the simulation
        // resolves to the last trading day's close even on a weekend or holiday. Clamp the
        // subtraction so a near-MinValue `from` (e.g. ?from=0001-01-01) doesn't underflow.
        var priceWindowFrom =
            resolvedFrom > DateOnly.MinValue.AddDays(14)
                ? resolvedFrom.AddDays(-14)
                : DateOnly.MinValue;

        var stockIds = holdings
            .Where(h => h.OptionType == null && h.Value > 0)
            .Select(h => h.CommonStockId)
            .Distinct()
            .ToList();

        var priceRows =
            stockIds.Count == 0
                ? []
                : await LoadPriceRows(
                    _priceRepository.GetByStocks(stockIds, priceWindowFrom, resolvedTo)
                );

        var pricesByStock = priceRows
            .GroupBy(p => p.StockId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var benchmarkPrices = await LoadPriceRows(
            _priceRepository.GetByStock(benchmarkStock, priceWindowFrom, resolvedTo)
        );

        if (benchmarkPrices.Count == 0)
        {
            viewModel.Result.Reason =
                $"No price data available for benchmark {viewModel.Benchmark} in the requested window.";
            return viewModel;
        }

        var benchmarkArray = benchmarkPrices.ToArray();

        viewModel.Result = HoldingsBacktestCalculator.Calculate(
            snapshots,
            resolvedFrom,
            resolvedTo,
            priceOf: (stockId, date) => ForwardFill(pricesByStock, stockId, date),
            benchmarkPriceOf: date => ForwardFill(benchmarkArray, date)
        );

        return viewModel;
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

    private async Task<List<BacktestBenchmarkOption>> LoadBenchmarkOptions()
    {
        var options = new List<BacktestBenchmarkOption>();
        foreach (var ticker in CandidateBenchmarks)
        {
            var stock = await _stockRepository.GetByTicker(ticker);
            if (stock != null)
                options.Add(new BacktestBenchmarkOption { Ticker = ticker, Name = stock.Name });
        }
        return options;
    }

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
            var rebalance = date.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);
            if (rebalance < resolvedFrom)
                lastBeforeWindow = date;
            else if (rebalance <= resolvedTo)
                relevant.Add(date);
        }
        if (lastBeforeWindow.HasValue && !relevant.Contains(lastBeforeWindow.Value))
            relevant.Insert(0, lastBeforeWindow.Value);
        return relevant;
    }

    // OrderBy() must precede the projection — EF can't translate an OrderBy keyed off the
    // projected record's property because the constructor isn't translatable.
    private static Task<List<BacktestPriceRow>> LoadPriceRows(IQueryable<DailyStockPrice> query) =>
        query
            .OrderBy(p => p.Date)
            .Select(p => new BacktestPriceRow(p.CommonStockId, p.Date, p.AdjustedClose))
            .ToListAsync();

    private static decimal? ForwardFill(
        Dictionary<Guid, BacktestPriceRow[]> pricesByStock,
        Guid stockId,
        DateOnly date
    )
    {
        return pricesByStock.TryGetValue(stockId, out var series)
            ? ForwardFill(series, date)
            : null;
    }

    private static decimal? ForwardFill(BacktestPriceRow[] series, DateOnly date)
    {
        if (series.Length == 0)
            return null;
        // Binary search for the largest Date <= the requested date.
        var lo = 0;
        var hi = series.Length - 1;
        var matchIdx = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var midDate = series[mid].Date;
            if (midDate <= date)
            {
                matchIdx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return matchIdx < 0 ? null : series[matchIdx].Price;
    }

    private readonly record struct BacktestHoldingRow(
        DateOnly ReportDate,
        Guid CommonStockId,
        long Shares,
        long Value,
        Holdings.Data.Models.OptionType? OptionType
    );

    private readonly record struct BacktestPriceRow(Guid StockId, DateOnly Date, decimal Price);
}
