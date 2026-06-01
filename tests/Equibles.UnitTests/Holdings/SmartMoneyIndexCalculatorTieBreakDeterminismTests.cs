using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class SmartMoneyIndexCalculatorTieBreakDeterminismTests
{
    private static readonly Guid LowerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HigherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Contract (class summary + the sort chain's own comment): the ranking carries a
    // "deterministic tie-break so ties don't reorder run to run". When two stocks tie on
    // BOTH ranking keys — consensus count and average portfolio weight — and the cap admits
    // only one, the survivor must be fixed by stock id, not by the order the funds happened
    // to surface the stocks. Here both stocks are held by the same 2 funds at an identical
    // 50% weight (equal consensus, equal average), and HigherId is listed FIRST in every
    // fund's positions; the documented determinism still requires LowerId to win, proving the
    // outcome is independent of input/accumulation order. Oracle derived from the contract
    // before reading the OrderBy chain.
    [Fact]
    public void Compose_FullyTiedConstituents_BreaksTieByStockIdIndependentOfInputOrder()
    {
        var funds = new[]
        {
            Fund((HigherId, 50), (LowerId, 50)),
            Fund((HigherId, 50), (LowerId, 50)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(
            funds,
            maxConstituents: 1,
            minConsensus: 2
        );

        constituents.Should().ContainSingle();
        constituents[0].CommonStockId.Should().Be(LowerId);
        constituents[0].HeldByCount.Should().Be(2);
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
