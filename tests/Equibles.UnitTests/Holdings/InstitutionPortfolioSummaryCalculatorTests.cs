using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

public class InstitutionPortfolioSummaryCalculatorTests
{
    [Fact]
    public void Calculate_EmptyCurrentQuarter_ReturnsZeroAumAndStillCarriesMetadata()
    {
        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [],
            [],
            quartersReported: 4,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        result.ReportedAum.Should().Be(0);
        result.PositionCount.Should().Be(0);
        result.Top10ConcentrationPercent.Should().Be(0);
        result.QoQTurnoverPercent.Should().Be(0);
        result.QuartersReported.Should().Be(4);
        result.LatestReportDate.Should().Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public void Calculate_SingleHolding_AumAndPositionCountReflectIt()
    {
        var holding = MakeHolding(stockId: Guid.NewGuid(), shares: 1_000, value: 1_000_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [holding],
            [],
            quartersReported: 1,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: null
        );

        result.ReportedAum.Should().Be(1_000_000);
        result.PositionCount.Should().Be(1);
        result.Top10ConcentrationPercent.Should().Be(100.0);
        result.Top25ConcentrationPercent.Should().Be(100.0);
        result.QoQTurnoverPercent.Should().Be(0); // No prior quarter → turnover is undefined → 0.
    }

    [Fact]
    public void Calculate_MultipleHoldingsSameStock_AggregatesIntoOnePosition()
    {
        var stockId = Guid.NewGuid();
        var holding1 = MakeHolding(stockId, shares: 600, value: 600_000);
        var holding2 = MakeHolding(stockId, shares: 400, value: 400_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [holding1, holding2],
            [],
            quartersReported: 1,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: null
        );

        result.PositionCount.Should().Be(1);
        result.ReportedAum.Should().Be(1_000_000);
    }

    [Fact]
    public void Calculate_Top10Concentration_SumsTopTenByValueOverAum()
    {
        // 11 stocks: top 10 sum to 100_000 each = 1_000_000; 11th = 100 → AUM = 1_000_100.
        var holdings = Enumerable
            .Range(1, 10)
            .Select(_ => MakeHolding(Guid.NewGuid(), shares: 1_000, value: 100_000))
            .Append(MakeHolding(Guid.NewGuid(), shares: 1, value: 100))
            .ToList();

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            holdings,
            [],
            quartersReported: 1,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: null
        );

        result.PositionCount.Should().Be(11);
        result.ReportedAum.Should().Be(1_000_100);
        // 1_000_000 / 1_000_100 ≈ 99.99% → assert within tight tolerance.
        result.Top10ConcentrationPercent.Should().BeApproximately(99.99, precision: 0.01);
        result.Top25ConcentrationPercent.Should().Be(100.0);
    }

    [Fact]
    public void Calculate_TurnoverWithIncreasedPosition_CountsAbsoluteDeltaTimesPriceProxy()
    {
        var stockId = Guid.NewGuid();
        // Current quarter: 1_500 shares @ $1_500_000 → $1_000 per share proxy.
        var current = MakeHolding(stockId, shares: 1_500, value: 1_500_000);
        // Prior quarter: 1_000 shares (any value — only Shares is read on the prior side).
        var previous = MakeHolding(stockId, shares: 1_000, value: 1_000_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [current],
            [previous],
            quartersReported: 2,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        // |Δ shares| × proxy = 500 × $1_000 = $500_000.
        // turnover = 500_000 / (2 × 1_500_000) = 16.6%.
        result.QoQTurnoverPercent.Should().BeApproximately(16.6667, precision: 0.01);
    }

    [Fact]
    public void Calculate_TurnoverWithSoldOutPosition_FallsBackToZeroProxyForThatStock()
    {
        // Holder kept stock A unchanged AUM-wise but fully sold out of stock B.
        var stockA = Guid.NewGuid();
        var stockB = Guid.NewGuid();
        var currentA = MakeHolding(stockA, shares: 1_000, value: 1_000_000);
        var priorA = MakeHolding(stockA, shares: 1_000, value: 1_000_000);
        var priorB = MakeHolding(stockB, shares: 500, value: 500_000);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [currentA],
            [priorA, priorB],
            quartersReported: 2,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        // Stock A: no movement. Stock B: sold out → no current proxy → its $500k exit is
        // unavoidably missed without a price-history dependency. Acceptable per the calculator's
        // documented behavior; turnover ends up at 0% rather than a wrong number.
        result.QoQTurnoverPercent.Should().Be(0);
    }

    [Fact]
    public void Calculate_TurnoverWithZeroAum_ReturnsZeroNotDivideByZero()
    {
        var stockId = Guid.NewGuid();
        // Current quarter all zero-value (unusual but possible during a value-pending refresh).
        var current = MakeHolding(stockId, shares: 1_000, value: 0);

        var result = InstitutionPortfolioSummaryCalculator.Calculate(
            [current],
            [MakeHolding(stockId, shares: 500, value: 500_000)],
            quartersReported: 2,
            latestReportDate: new DateOnly(2024, 12, 31),
            previousReportDate: new DateOnly(2024, 9, 30)
        );

        result.ReportedAum.Should().Be(0);
        result.QoQTurnoverPercent.Should().Be(0);
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
