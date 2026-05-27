using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class FundOverlapCalculatorEmptyHoldingsTests
{
    private static readonly DateOnly Report = new(2024, 12, 31);

    [Fact]
    public void Calculate_TwoFundsBothHoldingNothing_ReturnsZeroOverlapNoDivideByZero()
    {
        // Existing `NoFunds_ReturnsEmptyResult` short-circuits via `funds.Count == 0`.
        // This test exercises a different gap: two funds DO exist, but each one
        // happens to report zero holdings — a real production state for a fund that
        // wound down or filed an empty 13F. The orchestrator then reaches the
        // `allStockIds.Count > 0 ? ... : 0` and `dollarWeightedDenominator > 0 ? ... : 0`
        // ternaries with empty inputs. A refactor that drops either guard would
        // surface NaN / +∞ (double / 0 → not an exception, but worse: silently
        // serialized to the API as JSON `NaN`, which is invalid JSON and tanks
        // the response). The contract pins both ratios at 0 — and crucially that
        // the orchestrator does not throw on the empty-fund-list traversal.
        var fundA = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Fund A",
            Cik = "C001",
        };
        var fundB = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Name = "Fund B",
            Cik = "C002",
        };

        var result = FundOverlapCalculator.Calculate(
            [
                (fundA, Array.Empty<InstitutionalHolding>()),
                (fundB, Array.Empty<InstitutionalHolding>()),
            ],
            Report
        );

        result.Funds.Should().HaveCount(2);
        result.UnionPositionCount.Should().Be(0);
        result.IntersectionPositionCount.Should().Be(0);
        result.JaccardSimilarityPercent.Should().Be(0);
        result.DollarWeightedOverlapPercent.Should().Be(0);
    }
}
