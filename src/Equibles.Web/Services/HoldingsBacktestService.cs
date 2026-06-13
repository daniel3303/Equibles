using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.BusinessLogic;
using Equibles.Web.ViewModels.Profiles;

namespace Equibles.Web.Services;

[Service]
public class HoldingsBacktestService
{
    public const string DefaultBenchmark = HoldingsCloneBacktestProvider.DefaultBenchmark;

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

    private readonly CommonStockRepository _stockRepository;
    private readonly HoldingsCloneBacktestProvider _backtestProvider;

    public HoldingsBacktestService(
        CommonStockRepository stockRepository,
        HoldingsCloneBacktestProvider backtestProvider
    )
    {
        _stockRepository = stockRepository;
        _backtestProvider = backtestProvider;
    }

    public async Task<BacktestViewModel> Execute(
        string cik,
        DateOnly? from,
        DateOnly? to,
        string benchmark
    )
    {
        var outcome = await _backtestProvider.Run(cik, from, to, benchmark);

        var viewModel = new BacktestViewModel
        {
            Cik = cik,
            Benchmark = outcome.Benchmark,
            RequestedFrom = from,
            RequestedTo = to,
            BenchmarkOptions = await LoadBenchmarkOptions(),
        };

        if (outcome.HolderNotFound)
        {
            viewModel.HolderNotFound = true;
            return viewModel;
        }
        viewModel.HolderName = outcome.HolderName;

        if (outcome.BenchmarkNotFound)
        {
            viewModel.BenchmarkNotFound = true;
            viewModel.ErrorMessage = $"Benchmark ticker '{outcome.Benchmark}' is not known.";
            return viewModel;
        }
        viewModel.BenchmarkName = outcome.BenchmarkName;
        viewModel.Result = outcome.Result;
        return viewModel;
    }

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
}
