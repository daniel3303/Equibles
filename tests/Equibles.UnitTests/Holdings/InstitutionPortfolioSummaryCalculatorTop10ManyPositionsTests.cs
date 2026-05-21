using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTop10ManyPositionsTests
{
    // Sibling to InstitutionPortfolioSummaryCalculatorTop25ManyPositionsTests.
    // The existing 11-position test pins Top10 at 99.99% with a precision of
    // 0.01 — wide enough that a refactor flipping `Take(10)` to `Take(25)` or
    // `Take(all)` lands inside the tolerance and the pin still passes. Pin
    // the Top10 arithmetic on a 30-position equal-value portfolio so the
    // expected value (~33.33%) is observably distinct from both Top25
    // (~83.33%) and a "sum everything" regression (100%).
    [Fact]
    public void Calculate_Top10ConcentrationWithMoreThan10Positions_SumsLargest10ByValueOverAum()
    {
        var holdings = Enumerable
            .Range(0, 30)
            .Select(_ => MakeHolding(Guid.NewGuid(), shares: 100, value: 100))
            .ToList();

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            holdings,
            [],
            quartersReported: 1,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: null
        );

        // 30 × $100 = $3000 AUM. Top10 = $1000 / $3000 ≈ 33.33%.
        // A flipped Take(10) → Take(25) renders ≈ 83.33; Take(everything)
        // renders 100 — both observably wrong against this assertion.
        result.PositionCount.Should().Be(30);
        result.ReportedAum.Should().Be(3000);
        result.Top10ConcentrationPercent.Should().BeApproximately(33.33, precision: 0.01);
    }

    private static InstitutionalHolding MakeHolding(Guid stockId, long shares, long value) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = Guid.NewGuid(),
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };
}
