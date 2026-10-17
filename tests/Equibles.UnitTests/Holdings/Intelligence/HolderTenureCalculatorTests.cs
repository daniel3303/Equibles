using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderTenureCalculatorTests
{
    [Theory]
    [InlineData("2020-03-31", 0)]
    [InlineData("2020-06-30", 1)]
    [InlineData("2026-06-30", 25)]
    [InlineData("2035-12-31", 63)]
    [InlineData("2019-12-31", null)]
    [InlineData("2036-03-31", null)]
    [InlineData("2026-05-31", null)]
    [InlineData("2026-06-29", null)]
    public void QuarterIndex_CountsCoveredQuarterEnds(string date, int? expected)
    {
        HolderTenureCalculator.QuarterIndex(DateOnly.Parse(date)).Should().Be(expected);
    }

    [Fact]
    public void Tenure_CountsTheRunOfHeldQuarters()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(3, 4, 5, 6), Mask(0, 1, 2, 3, 4, 5, 6), 6);

        tenure.Should().Be(new HolderTenure(4, false));
    }

    [Fact]
    public void Tenure_NotHeldThatQuarter_IsNull()
    {
        HolderTenureCalculator.Tenure(Mask(3, 4), Mask(3, 4, 5), 5).Should().BeNull();
    }

    [Fact]
    public void Tenure_BridgesOneUnfiledQuarter()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(2, 3, 5, 6), Mask(1, 2, 3, 5, 6), 6);

        tenure.Should().Be(new HolderTenure(5, false));
    }

    [Fact]
    public void Tenure_TwoUnfiledQuartersEndTheRun()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(1, 2, 5, 6), Mask(1, 2, 5, 6), 6);

        tenure.Should().Be(new HolderTenure(2, false));
    }

    [Fact]
    public void Tenure_AFiledQuarterWithoutTheSecurityEndsTheRun()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(2, 3, 5, 6), Mask(2, 3, 4, 5, 6), 6);

        tenure.Should().Be(new HolderTenure(2, false));
    }

    [Fact]
    public void Tenure_AnUnfiledGapNotHeldBeforeEndsTheRun()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(5, 6), Mask(3, 5, 6), 6);

        tenure.Should().Be(new HolderTenure(2, false));
    }

    [Fact]
    public void Tenure_ReachingTheFirstCoveredQuarterIsCensored()
    {
        HolderTenureCalculator
            .Tenure(Mask(0, 1, 2, 3), Mask(0, 1, 2, 3), 3)
            .Should()
            .Be(new HolderTenure(4, true));
        HolderTenureCalculator
            .Tenure(Mask(0, 2, 3), Mask(0, 2, 3), 3)
            .Should()
            .Be(new HolderTenure(4, true));
    }

    [Fact]
    public void Tenure_AnUnfiledFirstQuarterIsNotCensored()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(1, 2, 3), Mask(1, 2, 3), 3);

        tenure.Should().Be(new HolderTenure(3, false));
    }

    [Fact]
    public void Tenure_ReadsTheLastCoveredQuarter()
    {
        var tenure = HolderTenureCalculator.Tenure(Mask(62, 63), Mask(61, 62, 63), 63);

        tenure.Should().Be(new HolderTenure(2, false));
    }

    [Fact]
    public void Summarize_WeighsTenureByValue()
    {
        var summary = HolderTenureCalculator.Summarize([
            (100m, new HolderTenure(4, false)),
            (300m, new HolderTenure(8, true)),
            (0m, new HolderTenure(40, true)),
        ]);

        summary.ValueWeightedQuarters.Should().Be(7m);
        summary.CensoredValueShare.Should().Be(0.75m);
        HolderTenureCalculator.Summarize([]).Should().Be(new HolderTenureSummary(null, null));
    }

    private static long Mask(params int[] quarters) =>
        quarters.Aggregate(0L, HolderTenureCalculator.Set);
}
