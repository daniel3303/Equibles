using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class BacktestCoverageIsRepresentativeTests
{
    private static BacktestCoverage Coverage(decimal average, decimal minimum, int quarters = 12) =>
        new()
        {
            AverageLongPercent = average,
            MinimumLongPercent = minimum,
            QuartersMeasured = quarters,
        };

    [Fact]
    public void IsRepresentative_PlainLongBook_IsTrue()
    {
        // The ordinary filer must pass cleanly. If this ever went false, every clone on the
        // platform would carry a warning — and a warning that is always on is one nobody reads.
        Coverage(100m, 100m).IsRepresentative.Should().BeTrue();
    }

    [Fact]
    public void IsRepresentative_ScionShapedBook_IsFalse()
    {
        // Scion's trailing three years: about 58% of reported value tracked on average, and as
        // little as 4% in the quarter that supplied most of the return. This is the case that
        // produced a +102.5% headline for a manager whose actual result was negative.
        Coverage(58m, 4m).IsRepresentative.Should().BeFalse();
    }

    [Fact]
    public void IsRepresentative_HealthyAverageButOneCollapsedQuarter_IsFalse()
    {
        // The reason both bounds exist. A book that is fully long for years and then almost
        // entirely options for one stretch still averages well, and that one stretch is exactly
        // where an outlier return comes from. An average-only rule would wave this through.
        Coverage(85m, 5m).IsRepresentative.Should().BeFalse();
    }

    [Fact]
    public void IsRepresentative_EveryQuarterAcceptableButAverageThin_IsFalse()
    {
        // The mirror case: no single quarter collapses, but a persistent minority of the book is
        // untracked in all of them. A minimum-only rule would wave this through.
        Coverage(55m, 52m).IsRepresentative.Should().BeFalse();
    }

    [Fact]
    public void IsRepresentative_ExactlyOnBothThresholds_IsTrue()
    {
        // The bounds are inclusive. Pinned because an off-by-one here silently reclassifies every
        // filer sitting on the line, in whichever direction the mistake goes.
        Coverage(
            BacktestCoverage.RepresentativeAveragePercent,
            BacktestCoverage.RepresentativeMinimumPercent
        )
            .IsRepresentative.Should()
            .BeTrue();
    }

    [Fact]
    public void IsRepresentative_NothingMeasured_IsFalse()
    {
        // With no quarters measured the two percentages are zero, which must not read as "0% is
        // below the bar so warn" by accident — it has to be false because nothing was established.
        // A caller treating an unmeasured result as representative would show the headline for a
        // filer we know nothing about.
        Coverage(0m, 0m, quarters: 0).IsRepresentative.Should().BeFalse();
    }
}
