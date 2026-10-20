using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class StockHolderSignalsCalculatorTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    [Fact]
    public void Build_SumsHoldersAndMeasuresConvictionAndWeights()
    {
        var stock = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        // A concentrated holder: the stock is its largest position, 50% of its book.
        calculator.Add(
            Holder(1_000, [new(stock, 10, 500), new(new(Guid.NewGuid(), null), 10, 500)])
        );
        // A broad holder: the stock is its smallest of eleven positions, 1% of its book.
        var others = Enumerable
            .Range(0, 10)
            .Select(_ => new HolderEquityPosition(new(Guid.NewGuid(), null), 10, 990 / 10))
            .ToList();
        calculator.Add(Holder(1_000, [new(stock, 30, 10), .. others]));

        var row = calculator
            .Build(Quarter, new StockHolderSignalsQuarterInputs())
            .Single(row => row.EquityIssuerId == stock.IssuerId);

        row.Filers.Should().Be(2);
        row.Shares.Should().Be(40);
        row.Value.Should().Be(510);
        row.ConvictionHolders.Should().Be(1);
        row.HoldersAboveFivePercent.Should().Be(1);
        row.AverageWeight.Should().Be((0.5m + 0.01m) / 2);
        row.MedianWeight.Should().BeApproximately(0.255m, 0.0000001m);
        row.TopTenHolderShare.Should().Be(1m);
        row.Herfindahl.Should().BeApproximately((100m + 900m) / 1600m, 0.0000001m);
    }

    [Fact]
    public void Build_CrowdingDividesOnlyAConsolidatedPrimaryCount()
    {
        var crowded = new HolderSecurityKey(Guid.NewGuid(), null);
        var calm = new HolderSecurityKey(Guid.NewGuid(), null);
        var multiClass = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        calculator.Add(
            Holder(
                3_000,
                [new(crowded, 300, 1_000), new(calm, 10, 1_000), new(multiClass, 50, 1_000)],
                turnover: HolderTurnoverBand.High
            )
        );
        calculator.Add(Holder(1_000, [new(calm, 500, 1_000)], privateFunds: true));

        var rows = calculator
            .Build(
                Quarter,
                new StockHolderSignalsQuarterInputs
                {
                    SharesOutstanding = new Dictionary<
                        Guid,
                        (long, DateOnly, SharesOutstandingBasis)
                    >
                    {
                        [crowded.IssuerId] = (1_000, Quarter, SharesOutstandingBasis.Consolidated),
                        [calm.IssuerId] = (10_000, Quarter, SharesOutstandingBasis.Consolidated),
                        [multiClass.IssuerId] = (100, Quarter, SharesOutstandingBasis.ClassSum),
                    },
                }
            )
            .ToDictionary(row => row.EquityIssuerId);

        rows[crowded.IssuerId].CrowdedShares.Should().Be(300);
        rows[crowded.IssuerId].CrowdingPercentile.Should().Be(100m);
        rows[calm.IssuerId].CrowdedShares.Should().Be(510);
        rows[calm.IssuerId].PrivateFundAdviserShares.Should().Be(500);
        rows[calm.IssuerId].CrowdingPercentile.Should().Be(50m);
        rows[multiClass.IssuerId].CrowdingPercentile.Should().BeNull();
        StockHolderSignalsCalculator
            .CountDividesListing(rows[multiClass.IssuerId])
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Build_EstimatedCostAndUnderwaterValue_ReadOnlyEstimatedSlots()
    {
        var stock = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        // Bought at 20, worth 10 a share: underwater.
        calculator.Add(
            Holder(1_000, [new(stock, 100, 1_000)], cost: HolderCostSlot.Estimated(20m))
        );
        // Bought at 5: in profit.
        calculator.Add(Holder(1_000, [new(stock, 100, 1_000)], cost: HolderCostSlot.Estimated(5m)));
        // Held before its history: no cost.
        calculator.Add(Holder(1_000, [new(stock, 100, 1_000)], cost: HolderCostSlot.UnknownEntry));

        var row = calculator.Build(Quarter, new StockHolderSignalsQuarterInputs()).Single();

        row.PricedShares.Should().Be(200);
        row.EstimatedCostPerShare.Should().Be(12.5m);
        row.UnderwaterValueShare.Should().Be(0.5m);
    }

    [Fact]
    public void Build_ActiveLikeForLike_ReadsOnlyActiveFilersWithAComparableQuarter()
    {
        var stock = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        calculator.Add(Holder(1_000, [new(stock, 150, 1_000)], previous: [new(stock, 100, 700)]));
        calculator.Add(
            Holder(
                1_000,
                [new(stock, 900, 1_000)],
                previous: [new(stock, 100, 100)],
                style: HolderStyleBand.IndexLike
            )
        );
        calculator.Add(Holder(1_000, [new(stock, 50, 300)]));

        var row = calculator.Build(Quarter, new StockHolderSignalsQuarterInputs()).Single();

        row.ActiveLikeForLikeShares.Should().Be(150);
        row.ActiveLikeForLikePreviousShares.Should().Be(100);
    }

    [Fact]
    public void Build_TopScoredBuying_IsNullUnlessMeasured()
    {
        var stock = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        calculator.Add(
            Holder(1_000, [new(stock, 100, 1_000)], topScored: true, trades: [new(stock, 400m, 0m)])
        );

        calculator
            .Build(Quarter, new StockHolderSignalsQuarterInputs())
            .Single()
            .TopScoreNetBuyValue.Should()
            .BeNull();
        var measured = calculator
            .Build(Quarter, new StockHolderSignalsQuarterInputs { MeasuresTopScored = true })
            .Single();
        measured.TopScoreNetBuyValue.Should().Be(400m);
        measured.TopScoreBuyers.Should().Be(1);
        measured.TopScoreSellers.Should().Be(0);
    }

    [Fact]
    public void Build_TenureBands_SplitValueAndKeepCensoredRunsApart()
    {
        var stock = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        calculator.Add(Holder(1_000, [new(stock, 1, 100)], tenure: new HolderTenure(2, false)));
        calculator.Add(Holder(1_000, [new(stock, 1, 200)], tenure: new HolderTenure(6, false)));
        calculator.Add(Holder(1_000, [new(stock, 1, 300)], tenure: new HolderTenure(14, true)));
        calculator.Add(Holder(1_000, [new(stock, 1, 400)], tenure: new HolderTenure(8, true)));

        var row = calculator.Build(Quarter, new StockHolderSignalsQuarterInputs()).Single();

        row.TenureShortValue.Should().Be(100);
        row.TenureMediumValue.Should().Be(200);
        row.TenureLongValue.Should().Be(300);
        row.TenureCensoredValue.Should().Be(400);
        row.ValueWeightedTenureQuarters.Should().Be((200m + 1_200m + 4_200m + 3_200m) / 1_000m);
    }

    private static HolderSignalsContribution Holder(
        long aum,
        List<HolderEquityPosition> current,
        List<HolderEquityPosition> previous = null,
        HolderStyleBand style = HolderStyleBand.Concentrated,
        HolderTurnoverBand turnover = HolderTurnoverBand.Low,
        bool privateFunds = false,
        bool topScored = false,
        HolderCostSlot? cost = null,
        HolderTenure? tenure = null,
        List<HolderSecurityTrade> trades = null
    ) =>
        new()
        {
            InstitutionalHolderId = Guid.NewGuid(),
            EquityAum = aum,
            StyleBand = style,
            TurnoverBand = turnover,
            AdvisesPrivateFunds = privateFunds,
            IsTopScored = topScored,
            Book = new HolderQuarterBook
            {
                Current = current,
                Previous = previous,
                CostSlots = cost is { } slot
                    ? current.ToDictionary(position => position.Security, _ => slot)
                    : new Dictionary<HolderSecurityKey, HolderCostSlot>(),
                Tenures = tenure is { } run
                    ? current.ToDictionary(position => position.Security, _ => run)
                    : new Dictionary<HolderSecurityKey, HolderTenure>(),
                SecurityTrades = trades ?? [],
            },
        };
}
