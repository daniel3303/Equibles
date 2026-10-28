using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class SplitBasisShareCountRepairerTests
{
    private static readonly DateOnly ReportDate = new(2026, 9, 30);
    private static readonly DateOnly FilingDate = new(2026, 11, 13);

    // A 1.0106 stock dividend moves a count by 1%: both bases fit the price, so neither is judged.
    [Fact]
    public void StraddledFactor_RatioWithinThePriceBand_IsNotAStraddle()
    {
        var factor = SplitBasisShareCountRepairer.StraddledFactor(
            ReportDate,
            FilingDate,
            asOf => asOf < new DateOnly(2026, 11, 3) ? 1.0106m : 1m
        );

        factor.Should().BeNull();
    }

    [Fact]
    public void StraddledFactor_UnresolvedBasisOnEitherDate_IsNotJudged()
    {
        SplitBasisShareCountRepairer
            .StraddledFactor(ReportDate, FilingDate, asOf => asOf == FilingDate ? null : 10m)
            .Should()
            .BeNull();
        SplitBasisShareCountRepairer
            .StraddledFactor(ReportDate, FilingDate, asOf => asOf == ReportDate ? null : 1m)
            .Should()
            .BeNull();
    }

    [Fact]
    public void StraddledFactor_SplitBetweenReportAndFiling_IsItsRatio()
    {
        SplitBasisShareCountRepairer
            .StraddledFactor(ReportDate, FilingDate, asOf => asOf < FilingDate ? 4m : 1m)
            .Should()
            .Be(4m);
    }

    // A position that bought or sold during the quarter still fits the price on its own basis.
    [Theory]
    [InlineData(1_000_000L, 100_000_000L)]
    [InlineData(1_000_000L, 2_500_000L)]
    public void Classify_FiguresNotOffByTheRatio_AreConsistent(long shares, long filedValue)
    {
        SplitBasisShareCountRepairer
            .Classify(shares, filedValue, 100m, factor: 10m, neighbourShares: 1_000_000L)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData(950_000L, false)]
    [InlineData(110_000L, true)]
    public void Classify_TheNeighbourNamesWhichFigureMoved(long neighbourShares, bool countMoved)
    {
        SplitBasisShareCountRepairer
            .Classify(1_000_000L, 10_000_000L, 100m, factor: 10m, neighbourShares)
            .Should()
            .Be(
                countMoved
                    ? SplitBasisShareCountRepairer.Kind.PostSplitShares
                    : SplitBasisShareCountRepairer.Kind.PostSplitValue
            );
    }
}
