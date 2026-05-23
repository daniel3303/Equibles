using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Contract: PctChange = (CurrentShares - PreviousShares) / PreviousShares * 100.
/// When PreviousShares is 0, must return 0 (not throw DivideByZeroException).
/// </summary>
public class DoubleDownPositionPctChangeTests
{
    [Fact]
    public void PctChange_PreviousSharesZero_ReturnsZeroInsteadOfThrowing()
    {
        var position = new DoubleDownPosition { CurrentShares = 500, PreviousShares = 0 };

        var act = () => position.PctChange;

        act.Should().NotThrow();
        position.PctChange.Should().Be(0);
    }
}
