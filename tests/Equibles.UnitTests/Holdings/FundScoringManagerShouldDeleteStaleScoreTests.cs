using System.Reflection;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

// ShouldDeleteStaleScore is ScoreHolder's prune gate when a backtest result can't be stored.
// It must evict the existing stored score when the filer's window is too short to annualize
// (CAGR null) EVEN THOUGH it still has 13F snapshots — otherwise a pre-floor annualized
// artifact (a ~19-day window annualized to +96,699%) keeps dominating the alpha leaderboard
// (#3407). A transient out-of-range/non-finite result (CAGR present but huge) keeps the
// previous score so a one-cycle data hiccup never wipes the leaderboard.
public class FundScoringManagerShouldDeleteStaleScoreTests
{
    private static readonly MethodInfo Method = typeof(FundScoringManager).GetMethod(
        "ShouldDeleteStaleScore",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static bool ShouldDelete(BacktestResult result, bool has13FSnapshots) =>
        (bool)Method.Invoke(null, [result, has13FSnapshots]);

    [Fact]
    public void TooShortToAnnualize_WithSnapshots_DeletesStaleScore()
    {
        // Below the annualization floor: the backtest produced points but CAGR is null.
        var tooShort = new BacktestResult
        {
            Points = [new BacktestPoint()],
            PortfolioSummary = new BacktestStrategySummary { CagrPercent = null },
            BenchmarkSummary = new BacktestStrategySummary { CagrPercent = null },
        };

        ShouldDelete(tooShort, has13FSnapshots: true)
            .Should()
            .BeTrue("a too-short window leaves a stale annualized artifact that must be pruned");
    }

    [Fact]
    public void OutOfRangeButAnnualized_WithSnapshots_KeepsScore()
    {
        // CAGRs are present (the window did annualize) but out of range — treated as a
        // transient hiccup, so the previous score is kept.
        var outOfRange = new BacktestResult
        {
            Points = [new BacktestPoint()],
            PortfolioSummary = new BacktestStrategySummary { CagrPercent = decimal.MaxValue },
            BenchmarkSummary = new BacktestStrategySummary { CagrPercent = 9m },
        };

        ShouldDelete(outOfRange, has13FSnapshots: true)
            .Should()
            .BeFalse(
                "an out-of-range but annualized result is transient and must not wipe the score"
            );
    }

    [Fact]
    public void BenchmarkCagrNullButPortfolioAnnualized_WithSnapshots_KeepsScore()
    {
        // Portfolio annualized fine but the benchmark CAGR is null (e.g. SPY prices lagged the
        // window). That is a transient benchmark data gap, not a too-short portfolio — pruning
        // here would wipe the leaderboard whenever the price feed lags, so the score is kept.
        var benchmarkGap = new BacktestResult
        {
            Points = [new BacktestPoint()],
            PortfolioSummary = new BacktestStrategySummary { CagrPercent = 12m },
            BenchmarkSummary = new BacktestStrategySummary { CagrPercent = null },
        };

        ShouldDelete(benchmarkGap, has13FSnapshots: true)
            .Should()
            .BeFalse("a null benchmark CAGR with a real portfolio CAGR is a transient gap");
    }

    [Fact]
    public void NoSnapshots_DeletesStaleScore()
    {
        ShouldDelete(new BacktestResult(), has13FSnapshots: false)
            .Should()
            .BeTrue("a filer with no 13F snapshots structurally can't be scored");
    }
}
