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
    public void Tenure_BridgesEveryConfidentialQuarterBackToCoverageStart()
    {
        var everyOther = Mask(0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20);

        HolderTenureCalculator
            .Tenure(everyOther, everyOther, 20)
            .Should()
            .Be(new HolderTenure(21, true));
    }

    [Fact]
    public void Tenure_CensorsAtALaterCoverageStart()
    {
        var held = Mask(2, 3, 4, 5, 6, 7, 8);

        HolderTenureCalculator.Tenure(held, held, 8, 4).Should().Be(new HolderTenure(5, true));
        HolderTenureCalculator.Tenure(held, held, 4, 4).Should().Be(new HolderTenure(1, true));
        HolderTenureCalculator
            .Tenure(Mask(5, 6, 7, 8), Mask(5, 6, 7, 8), 8, 4)
            .Should()
            .Be(new HolderTenure(4, false));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(64, 0)]
    [InlineData(5, 64)]
    [InlineData(3, 4)]
    public void Tenure_RefusesQuartersOutsideTheMask(int quarter, int firstCoveredQuarter)
    {
        var act = () => HolderTenureCalculator.Tenure(-1L, -1L, quarter, firstCoveredQuarter);

        act.Should().Throw<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() => HolderTenureCalculator.Set(0, 64))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Tenure_MatchesAQuarterByQuarterReference()
    {
        var random = new Random(11);
        for (var trial = 0; trial < 5_000; trial++)
        {
            long held = 0,
                filed = 0;
            for (var quarter = 0; quarter < HolderTenureCalculator.MaximumQuarters; quarter++)
            {
                if (random.NextDouble() < 0.6)
                    held = HolderTenureCalculator.Set(held, quarter);
                if (random.NextDouble() < 0.5)
                    filed = HolderTenureCalculator.Set(filed, quarter);
            }
            filed |= held;
            var first = random.Next(0, 10);
            var at = random.Next(first, HolderTenureCalculator.MaximumQuarters);

            HolderTenureCalculator
                .Tenure(held, filed, at, first)
                .Should()
                .Be(Reference(held, filed, at, first));
        }
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

    // Walks back from held quarter to held quarter; the run starts where a gap is filed, too long
    // or runs off the covered range, and is censored only when it starts at the first quarter.
    private static HolderTenure? Reference(long held, long filed, int quarter, int first)
    {
        bool Held(int index) => ((held >> index) & 1) == 1;
        bool Filed(int index) => ((filed >> index) & 1) == 1;
        if (!Held(quarter))
            return null;
        var start = quarter;
        while (true)
        {
            var before = start - 1;
            while (before >= first && !Held(before) && !Filed(before))
                before--;
            var bridged = start - before - 1;
            if (
                before < first
                || !Held(before)
                || bridged > HolderTenureCalculator.MaximumBridgedQuarters
            )
                return new HolderTenure(quarter - start + 1, start == first);
            start = before;
        }
    }

    private static long Mask(params int[] quarters) =>
        quarters.Aggregate(0L, HolderTenureCalculator.Set);
}
