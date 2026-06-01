using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

public class SmartMoneyIndexCalculatorConsensusDominatesWeightTests
{
    private static readonly Guid HighConsensus = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HighWeight = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FillerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid FillerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FillerC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // Contract (class summary): "ranks them by consensus count THEN by average portfolio
    // weight". Consensus is the primary key, so a stock held by more funds must outrank a
    // stock held by fewer funds even when the latter carries a far higher average weight.
    // This isolates the discriminating case the existing ranking test doesn't: there the
    // top-consensus stock also had the top weight, so a weight-first sort would pass anyway.
    // Here HighConsensus is held by 3 funds at 5% each while HighWeight is held by 2 funds
    // at 100% each; with a single-slot cap, only HighConsensus may survive. Oracle from the
    // documented ranking order, derived before reading the OrderBy chain.
    [Fact]
    public void Compose_HigherConsensusLowerWeight_OutranksLowerConsensusHigherWeight()
    {
        var funds = new[]
        {
            Fund((HighConsensus, 5), (FillerA, 95)),
            Fund((HighConsensus, 5), (FillerB, 95)),
            Fund((HighConsensus, 5), (FillerC, 95)),
            Fund((HighWeight, 100)),
            Fund((HighWeight, 100)),
        };

        var constituents = SmartMoneyIndexCalculator.Compose(
            funds,
            maxConstituents: 1,
            minConsensus: 2
        );

        constituents.Should().ContainSingle();
        constituents[0].CommonStockId.Should().Be(HighConsensus);
        constituents[0].HeldByCount.Should().Be(3);
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
