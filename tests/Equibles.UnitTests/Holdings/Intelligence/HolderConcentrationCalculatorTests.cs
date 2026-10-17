using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderConcentrationCalculatorTests
{
    [Fact]
    public void Measure_WeighsByIssuerAndCountsUnvaluedStocks()
    {
        var sibling = Guid.NewGuid();
        // Eleven issuers worth 1..11, one of them split across two share classes, plus one
        // unvalued stock.
        var positions = Enumerable
            .Range(1, 10)
            .Select(value => new HolderEquityPosition(new(Guid.NewGuid(), null), 10, value))
            .Append(new(new(sibling, null), 10, 5))
            .Append(new(new(sibling, "SIB.B"), 10, 6))
            .Append(new(new(Guid.NewGuid(), null), 10, null))
            .ToList();

        var concentration = HolderConcentrationCalculator.Measure(positions);

        concentration.StockCount.Should().Be(12);
        // Total 66; the top ten leave out the issuer worth 1.
        concentration.TopTenWeight.Should().BeApproximately(65m / 66m, 0.0000000001m);
        var herfindahl =
            Enumerable.Range(1, 10).Sum(v => (decimal)v * v) / (66m * 66m) + 121m / (66m * 66m);
        concentration.Herfindahl.Should().BeApproximately(herfindahl, 0.0000000001m);
        concentration.EffectivePositions.Should().BeApproximately(1m / herfindahl, 0.0000001m);
    }

    [Fact]
    public void Measure_NoValuedPosition_LeavesWeightsUnknown()
    {
        var concentration = HolderConcentrationCalculator.Measure([
            new(new(Guid.NewGuid(), null), 10, null),
            new(new(Guid.NewGuid(), null), 0, 0),
        ]);

        concentration.StockCount.Should().Be(1);
        concentration.TopTenWeight.Should().BeNull();
        concentration.Herfindahl.Should().BeNull();
        concentration.EffectivePositions.Should().BeNull();
    }
}
