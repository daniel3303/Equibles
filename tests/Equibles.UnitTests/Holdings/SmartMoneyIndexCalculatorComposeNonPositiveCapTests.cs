using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class SmartMoneyIndexCalculatorComposeNonPositiveCapTests
{
    private static readonly Guid StockA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StockB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Compose_NonPositiveMaxConstituents_ClampsToSingleTopConstituent()
    {
        // Degenerate cap guard: a caller passing a computed maxConstituents <= 0 must not get a
        // silently empty index. The documented guard clamps the cap to at least one, so the single
        // highest-conviction stock (A, the heavier weight) survives. A naive cap-at-zero would
        // instead Take(0) and return [], which this pins against.
        var funds = new[] { Fund((StockA, 70), (StockB, 30)), Fund((StockA, 70), (StockB, 30)) };

        var constituents = SmartMoneyIndexCalculator.Compose(
            funds,
            maxConstituents: 0,
            minConsensus: 1
        );

        constituents.Should().ContainSingle();
        constituents[0].CommonStockId.Should().Be(StockA);
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
