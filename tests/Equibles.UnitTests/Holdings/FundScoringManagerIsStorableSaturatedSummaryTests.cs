using System.Reflection;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class FundScoringManagerIsStorableSaturatedSummaryTests
{
    // IsStorable is the gate (ScoreHolder, line ~73) that decides whether a backtest result
    // is persisted. HoldingsBacktestCalculator.ComputeCagr deliberately *saturates* to
    // decimal.MaxValue on an extreme single-window move rather than aborting — so a result
    // can legitimately carry decimal.MaxValue in CagrPercent. That value is meaningless to
    // store and would blow past any sane column range, so IsStorable must reject any summary
    // field at that magnitude (MaxStorableMagnitude ~1e13) while accepting normal results.
    [Fact]
    public void IsStorable_SaturatedCagr_IsRejectedWhileNormalResultIsAccepted()
    {
        var method = typeof(FundScoringManager).GetMethod(
            "IsStorable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var normal = new BacktestResult
        {
            PortfolioSummary = new BacktestStrategySummary
            {
                TotalReturnPercent = 42m,
                CagrPercent = 12m,
                MaxDrawdownPercent = -8m,
            },
            BenchmarkSummary = new BacktestStrategySummary
            {
                TotalReturnPercent = 30m,
                CagrPercent = 9m,
                MaxDrawdownPercent = -10m,
            },
        };
        var saturated = new BacktestResult
        {
            PortfolioSummary = new BacktestStrategySummary { CagrPercent = decimal.MaxValue },
            BenchmarkSummary = new BacktestStrategySummary(),
        };

        ((bool)method.Invoke(null, [normal])).Should().BeTrue("all summary fields are in range");
        ((bool)method.Invoke(null, [saturated]))
            .Should()
            .BeFalse("a CAGR saturated to decimal.MaxValue must not be persisted");
    }
}
