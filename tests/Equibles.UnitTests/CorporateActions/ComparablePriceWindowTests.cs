using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class ComparablePriceWindowTests
{
    private static readonly DateOnly Start = new(2025, 8, 10);
    private static readonly DateOnly End = new(2026, 8, 10);

    [Fact]
    public void Resolve_NoSplitInsideRange_PreservesRequestedWindow()
    {
        var window = ComparablePriceWindow.Resolve(
            Start,
            End,
            [Split(Start), Split(End.AddDays(1))]
        );

        window.Start.Should().Be(Start);
        window.End.Should().Be(End);
        window.SplitBoundaryDate.Should().BeNull();
        window.IsSplitLimited.Should().BeFalse();
    }

    [Fact]
    public void Resolve_SplitsInsideRange_StartsAtLatestSplit()
    {
        var earlier = Start.AddMonths(2);
        var latest = End.AddDays(-7);

        var window = ComparablePriceWindow.Resolve(Start, End, [Split(earlier), Split(latest)]);

        window.RequestedStart.Should().Be(Start);
        window.Start.Should().Be(latest);
        window.SplitBoundaryDate.Should().Be(latest);
        window.IsSplitLimited.Should().BeTrue();
    }

    [Fact]
    public void Resolve_SplitOnEnd_IsInsideWindow()
    {
        var window = ComparablePriceWindow.Resolve(Start, End, [Split(End)]);

        window.Start.Should().Be(End);
        window.SplitBoundaryDate.Should().Be(End);
    }

    [Fact]
    public void Resolve_StartAfterEnd_Throws()
    {
        var resolve = () => ComparablePriceWindow.Resolve(End, Start, []);

        resolve.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static StockSplit Split(DateOnly date) => new() { EffectiveDate = date };
}
