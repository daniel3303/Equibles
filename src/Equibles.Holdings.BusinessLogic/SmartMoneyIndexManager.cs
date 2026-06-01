using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
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
/// Builds a "smart-money index": takes the top-scoring funds for a window/benchmark (ranked by
/// alpha via <see cref="FundScoreRepository"/>), draws their highest-conviction common holdings
/// into an equal-weighted basket through <see cref="SmartMoneyIndexCalculator"/>, and tracks that
/// basket's forward performance against the benchmark with the look-ahead-safe
/// <see cref="HoldingsBacktestCalculator"/>.
/// <para>
/// The basket is constructed point-in-time from each fund's latest 13F portfolio and tracked
/// forward from the rebalance date (45 days after the freshest report date). The price loading
/// and forward-fill mirror <see cref="FundScoringManager"/>, kept self-contained so the index
/// has no dependency on the web host.
/// </para>
/// </summary>
[Service]
public class SmartMoneyIndexManager
{
    public const string DefaultBenchmark = FundScoringManager.DefaultBenchmark;
    public const int DefaultWindowYears = FundScoringManager.DefaultWindowYears;

    // Equal-weighted basket: every constituent gets the same nominal value so the backtest's
    // value-weighting collapses to equal weighting.
    private const long EqualWeightValue = 1;

    private readonly FundScoreRepository _fundScoreRepository;
    private readonly InstitutionalHolderRepository _holderRepository;
    private readonly InstitutionalHoldingRepository _holdingRepository;
    private readonly CommonStockRepository _stockRepository;
    private readonly DailyStockPriceRepository _priceRepository;

    public SmartMoneyIndexManager(
        FundScoreRepository fundScoreRepository,
        InstitutionalHolderRepository holderRepository,
        InstitutionalHoldingRepository holdingRepository,
        CommonStockRepository stockRepository,
        DailyStockPriceRepository priceRepository
    )
    {
        _fundScoreRepository = fundScoreRepository;
        _holderRepository = holderRepository;
        _holdingRepository = holdingRepository;
        _stockRepository = stockRepository;
        _priceRepository = priceRepository;
    }

    public async Task<SmartMoneyIndexResult> Build(
        DateOnly asOf,
        int topFunds = SmartMoneyIndexCalculator.DefaultTopFunds,
        int maxConstituents = SmartMoneyIndexCalculator.DefaultMaxConstituents,
        int minConsensus = SmartMoneyIndexCalculator.DefaultMinConsensus,
        int windowYears = DefaultWindowYears,
        string benchmarkTicker = DefaultBenchmark
    )
    {
        topFunds = Math.Max(1, topFunds);
        benchmarkTicker = TickerNormalizer.Normalize(benchmarkTicker);

        var result = new SmartMoneyIndexResult
        {
            RequestedTopFunds = topFunds,
            MaxConstituents = maxConstituents,
            MinConsensus = minConsensus,
            WindowYears = windowYears,
            BenchmarkTicker = benchmarkTicker,
            AsOf = asOf,
        };

        var benchmarkStock = await _stockRepository.GetByTicker(benchmarkTicker);
        if (benchmarkStock == null)
        {
            result.Reason = $"Benchmark ticker '{benchmarkTicker}' is not known.";
            return result;
        }
        result.BenchmarkName = benchmarkStock.Name;

        var topScores = await _fundScoreRepository
            .GetRankedByAlpha(windowYears, benchmarkTicker)
            .Take(topFunds)
            .ToListAsync();
        if (topScores.Count == 0)
        {
            result.Reason =
                "No fund scores are available for this window and benchmark yet — run the scoring worker first.";
            return result;
        }

        var holderIds = topScores.Select(s => s.InstitutionalHolderId).ToList();
        var holdersById = await _holderRepository
            .GetAll()
            .Where(h => holderIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id);

        var fundPortfolios = new List<BacktestQuarterSnapshot>();
        DateOnly? constructionDate = null;
        foreach (var score in topScores)
        {
            if (!holdersById.TryGetValue(score.InstitutionalHolderId, out var holder))
                continue;

            var snapshot = await LoadLatestPortfolio(holder);
            if (snapshot == null)
                continue;

            fundPortfolios.Add(snapshot);
            if (constructionDate == null || snapshot.ReportDate > constructionDate.Value)
                constructionDate = snapshot.ReportDate;
        }

        result.FundCount = fundPortfolios.Count;
        if (fundPortfolios.Count == 0)
        {
            result.Reason = "The top-scoring funds have no holdings on file.";
            return result;
        }

        var constituents = SmartMoneyIndexCalculator.Compose(
            fundPortfolios,
            maxConstituents,
            minConsensus
        );
        if (constituents.Count == 0)
        {
            result.Reason =
                $"No stock was held by at least {Math.Max(1, minConsensus)} of the top {fundPortfolios.Count} funds.";
            return result;
        }

        await PopulateConstituentDetails(constituents);
        result.Constituents = constituents;
        result.ConstructionDate = constructionDate;

        await RunBacktest(result, constituents, constructionDate.Value, asOf, benchmarkStock);
        return result;
    }

    private async Task<BacktestQuarterSnapshot> LoadLatestPortfolio(InstitutionalHolder holder)
    {
        var reportDates = await _holdingRepository.GetReportDatesByHolder(holder).ToListAsync();
        if (reportDates.Count == 0)
            return null;

        // GetReportDatesByHolder returns latest-first; the index reflects each fund's freshest 13F.
        var latest = reportDates[0];

        var positions = await _holdingRepository
            .GetByHolder(holder, latest)
            .Where(h => h.OptionType == null && h.Value > 0)
            .GroupBy(h => h.CommonStockId)
            .Select(g => new { StockId = g.Key, Value = g.Sum(h => h.Value) })
            .ToListAsync();
        if (positions.Count == 0)
            return null;

        return new BacktestQuarterSnapshot
        {
            ReportDate = latest,
            Positions = positions
                .Select(p => new BacktestPosition
                {
                    CommonStockId = p.StockId,
                    Value = p.Value,
                    IsOption = false,
                })
                .ToList(),
        };
    }

    private async Task PopulateConstituentDetails(
        IReadOnlyList<SmartMoneyIndexConstituent> constituents
    )
    {
        var stockIds = constituents.Select(c => c.CommonStockId).ToList();
        var stocksById = await _stockRepository
            .GetByIds(stockIds)
            .Select(s => new
            {
                s.Id,
                s.Ticker,
                s.Name,
            })
            .ToDictionaryAsync(s => s.Id);

        foreach (var constituent in constituents)
        {
            if (stocksById.TryGetValue(constituent.CommonStockId, out var stock))
            {
                constituent.Ticker = stock.Ticker;
                constituent.Name = stock.Name;
            }
        }
    }

    private async Task RunBacktest(
        SmartMoneyIndexResult result,
        IReadOnlyList<SmartMoneyIndexConstituent> constituents,
        DateOnly constructionDate,
        DateOnly asOf,
        CommonStock benchmarkStock
    )
    {
        var snapshot = new BacktestQuarterSnapshot
        {
            ReportDate = constructionDate,
            Positions = constituents
                .Select(c => new BacktestPosition
                {
                    CommonStockId = c.CommonStockId,
                    Value = EqualWeightValue,
                    IsOption = false,
                })
                .ToList(),
        };

        var from =
            constructionDate.DayNumber
            > DateOnly.MaxValue.DayNumber - HoldingsBacktestCalculator.RebalanceDelayDays
                ? DateOnly.MaxValue
                : constructionDate.AddDays(HoldingsBacktestCalculator.RebalanceDelayDays);

        var stockIds = constituents.Select(c => c.CommonStockId).ToList();

        var backtest = await BacktestPriceLoader.RunBacktest(
            _priceRepository,
            [snapshot],
            stockIds,
            benchmarkStock,
            from,
            asOf
        );
        if (backtest == null)
        {
            result.Backtest.Reason =
                $"No price data available for benchmark {result.BenchmarkTicker} in the tracked window.";
            return;
        }

        result.Backtest = backtest;
    }
}
