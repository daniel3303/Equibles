using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTop25ManyPositionsTests
{
    // Existing tests for Top25 only touch portfolios with <= 25 positions, where
    // Take(25) is tautologically equivalent to Take(all) and the result collapses
    // to 100%. The actual `valuesDesc.Take(25).Sum() / AUM * 100` arithmetic is
    // only exercised when the portfolio EXCEEDS 25 positions — and a flip to
    // Take(10) (== Top10) or Take(all) (== 100%) would go undetected. Pin it on
    // a 30-position portfolio with equal values, where Top10 ≠ Top25 ≠ 100%.
    [Fact]
    public void Calculate_Top25ConcentrationWithMoreThan25Positions_SumsLargest25ByValueOverAum()
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
        // Top25 = $2500 / $3000 ≈ 83.33%. A flipped Take(25) → Take(10) collapses
        // Top25 to Top10 (33.33), Take(50) inflates it to 100 — both observably wrong.
        result.PositionCount.Should().Be(30);
        result.ReportedAum.Should().Be(3000);
        result.Top25ConcentrationPercent.Should().BeApproximately(83.33, precision: 0.01);
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
