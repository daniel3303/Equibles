using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class SmartMoneyIndexCalculatorTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StockC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StockD = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Compose_NoFunds_ReturnsEmpty()
    {
        var constituents = SmartMoneyIndexCalculator.Compose([]);

        constituents.Should().BeEmpty();
    }

    [Fact]
    public void Compose_StockBelowConsensusThreshold_IsExcluded()
    {
        // A held by 2 funds, B by only 1; require 2.
        var funds = new[] { Fund((StockA, 100), (StockB, 100)), Fund((StockA, 100)) };

        var constituents = SmartMoneyIndexCalculator.Compose(
            funds,
            maxConstituents: 10,
            minConsensus: 2
        );

        constituents.Should().ContainSingle();
        constituents[0].CommonStockId.Should().Be(StockA);
        constituents[0].HeldByCount.Should().Be(2);
    }

    [Fact]
    public void Compose_QualifyingStocks_AreEqualWeighted()
    {
        var funds = new[]
        {
            Fund((StockA, 100), (StockB, 100)),
            Fund((StockA, 100), (StockB, 100)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(funds, minConsensus: 2);

        constituents.Should().HaveCount(2);
        constituents.Should().OnlyContain(c => c.IndexWeightPercent == 50m);
    }

    [Fact]
    public void Compose_RanksByConsensusCountThenAverageWeight()
    {
        // A held by 3 funds, B and C by 2 each. Among B and C, C carries the higher conviction.
        var funds = new[]
        {
            Fund((StockA, 50), (StockB, 25), (StockC, 25)),
            Fund((StockA, 50), (StockB, 10), (StockC, 40)),
            Fund((StockA, 100)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(funds, minConsensus: 2);

        constituents.Select(c => c.CommonStockId).Should().Equal(StockA, StockC, StockB);
    }

    [Fact]
    public void Compose_AverageWeightPercent_IsMeanAcrossHoldingFundsOnly()
    {
        // B is held by funds 1 and 2 at 20% and 40%; fund 3 doesn't hold it, so it is not
        // averaged in. Expected average = (20 + 40) / 2 = 30%.
        var funds = new[]
        {
            Fund((StockA, 80), (StockB, 20)),
            Fund((StockA, 60), (StockB, 40)),
            Fund((StockA, 100)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(funds, minConsensus: 2);

        var b = constituents.Single(c => c.CommonStockId == StockB);
        b.AverageWeightPercent.Should().Be(30m);
    }

    [Fact]
    public void Compose_OptionRows_AreIgnored()
    {
        var funds = new[]
        {
            new BacktestQuarterSnapshot
            {
                ReportDate = new DateOnly(2025, 3, 31),
                Positions =
                [
                    new BacktestPosition
                    {
                        CommonStockId = StockA,
                        Value = 100,
                        IsOption = false,
                    },
                    new BacktestPosition
                    {
                        CommonStockId = StockB,
                        Value = 100,
                        IsOption = true,
                    },
                ],
            },
            Fund((StockA, 100)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(funds, minConsensus: 1);

        constituents.Select(c => c.CommonStockId).Should().NotContain(StockB);
        constituents.Single(c => c.CommonStockId == StockA).HeldByCount.Should().Be(2);
    }

    [Fact]
    public void Compose_NonPositiveValues_AreIgnored()
    {
        var funds = new[] { Fund((StockA, 100), (StockB, 0)), Fund((StockA, 100), (StockB, -5)) };

        var constituents = SmartMoneyIndexCalculator.Compose(funds, minConsensus: 1);

        constituents.Should().ContainSingle();
        constituents[0].CommonStockId.Should().Be(StockA);
    }

    [Fact]
    public void Compose_MoreQualifiersThanCap_TakesTopRankedUpToCap()
    {
        var funds = new[]
        {
            Fund((StockA, 40), (StockB, 30), (StockC, 20), (StockD, 10)),
            Fund((StockA, 40), (StockB, 30), (StockC, 20), (StockD, 10)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(
            funds,
            maxConstituents: 2,
            minConsensus: 2
        );

        // All four qualify on consensus; the cap keeps the two highest-conviction (A, B).
        constituents.Should().HaveCount(2);
        constituents.Select(c => c.CommonStockId).Should().Equal(StockA, StockB);
    }

    private static BacktestQuarterSnapshot Fund(params (Guid StockId, long Value)[] positions) =>
        new()
        {
            ReportDate = new DateOnly(2025, 3, 31),
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
