using Equibles.Holdings.Repositories.Models;

namespace Equibles.Web.ViewModels.Profiles;

public class BacktestViewModel
{
    public string Cik { get; set; }

    public string HolderName { get; set; }

    public string Benchmark { get; set; }

    public string BenchmarkName { get; set; }

    public DateOnly? RequestedFrom { get; set; }

    public DateOnly? RequestedTo { get; set; }

    public BacktestResult Result { get; set; } = new();

    public bool HolderNotFound { get; set; }

    public bool BenchmarkNotFound { get; set; }

    // The benchmark picker only suggests symbols the database can actually price —
    // populated by the service from `CommonStockRepository.GetByTicker` lookups.
    public List<BacktestBenchmarkOption> BenchmarkOptions { get; set; } = [];

    public string ErrorMessage { get; set; }
}
