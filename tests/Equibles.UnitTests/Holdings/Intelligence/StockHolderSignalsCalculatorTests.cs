using System.Globalization;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.BusinessLogic.Signals;
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
        for (var filer = 0; filer < StockHolderSignalsCalculator.MinimumRankedFilers; filer++)
            calculator.Add(
                Holder(
                    3_000,
                    [new(crowded, 30, 100), new(calm, 1, 100), new(multiClass, 5, 100)],
                    turnover: HolderTurnoverBand.High
                )
            );
        calculator.Add(Holder(1_000, [new(calm, 500, 1_000)], privateFunds: true));

        var rows = calculator
            .Build(
                Quarter,
                Counts(
                    (crowded, 1_000, Quarter, SharesOutstandingBasis.Consolidated),
                    (calm, 10_000, Quarter, SharesOutstandingBasis.Consolidated),
                    (multiClass, 100, Quarter, SharesOutstandingBasis.ClassSum)
                )
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
    public void Build_ACountBelowThe13FSharesDividesNothing()
    {
        // A shell's nominal 100 shares against 16M held, a count that predates an issuance (1,600
        // held against 1,000), and a count the holdings fit under.
        var shell = new HolderSecurityKey(Guid.NewGuid(), null);
        var issued = new HolderSecurityKey(Guid.NewGuid(), null);
        var counted = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        for (var filer = 0; filer < StockHolderSignalsCalculator.MinimumRankedFilers; filer++)
            calculator.Add(
                Holder(
                    3_000,
                    [
                        new(shell, 1_600_000, 100),
                        new(issued, 160, 100),
                        new(counted, 160, 100),
                    ],
                    turnover: HolderTurnoverBand.High
                )
            );

        var rows = calculator
            .Build(
                Quarter,
                Counts(
                    (shell, 100, Quarter, SharesOutstandingBasis.Consolidated),
                    (issued, 1_000, Quarter, SharesOutstandingBasis.Consolidated),
                    (counted, 2_000, Quarter, SharesOutstandingBasis.Consolidated)
                )
            )
            .ToDictionary(row => row.EquityIssuerId);

        foreach (var key in new[] { shell, issued })
        {
            var row = rows[key.IssuerId];
            StockHolderSignalsCalculator.CountDividesListing(row).Should().BeFalse();
            row.CrowdingPercentile.Should().BeNull();
            StockHolderSignalsMath.Crowding(row).Should().BeNull();
            StockHolderSignalsMath.InstitutionalShareOfOutstanding(row).Should().BeNull();
            StockHolderSignalsMath.InstitutionalShareOfFloat(row).Should().BeNull();
            StockHolderSignalsMath
                .OutstandingUnavailableReason(row)
                .Should()
                .StartWith("holders exceed shares outstanding");
        }
        rows[shell.IssuerId].SharesOutstanding.Should().Be(100, "the count is kept as read");
        rows[counted.IssuerId].CrowdingPercentile.Should().Be(100m, "it ranks alone");
        StockHolderSignalsMath
            .InstitutionalShareOfOutstanding(rows[counted.IssuerId])
            .Should()
            .Be(0.8m);
    }

    [Fact]
    public void Build_ACountMoreThanTwoQuartersOldDividesNothing()
    {
        var stale = new HolderSecurityKey(Guid.NewGuid(), null);
        var current = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        for (var filer = 0; filer < StockHolderSignalsCalculator.MinimumRankedFilers; filer++)
            calculator.Add(
                Holder(3_000, [new(stale, 10, 100), new(current, 10, 100)], turnover: HolderTurnoverBand.High)
            );
        var oldest = Quarter.AddDays(-StockHolderSignalsCalculator.MaximumCountAgeDays);

        var rows = calculator
            .Build(
                Quarter,
                Counts(
                    (stale, 1_000, oldest.AddDays(-1), SharesOutstandingBasis.Consolidated),
                    (current, 1_000, oldest, SharesOutstandingBasis.Consolidated)
                )
            )
            .ToDictionary(row => row.EquityIssuerId);

        var staleRow = rows[stale.IssuerId];
        StockHolderSignalsCalculator.CountIsStale(staleRow).Should().BeTrue();
        StockHolderSignalsMath.Crowding(staleRow).Should().BeNull();
        staleRow.CrowdingPercentile.Should().BeNull();
        StockHolderSignalsMath
            .OutstandingUnavailableReason(staleRow)
            .Should()
            .StartWith("share count stale")
            .And.Contain(oldest.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        StockHolderSignalsMath.Crowding(rows[current.IssuerId]).Should().Be(0.1m);
        rows[current.IssuerId].CrowdingPercentile.Should().Be(100m);
    }

    [Fact]
    public void Build_TooFewFilersReportCrowdingAndConvictionWithoutRanking()
    {
        var thin = new HolderSecurityKey(Guid.NewGuid(), null);
        var broad = new HolderSecurityKey(Guid.NewGuid(), null);
        var calculator = new StockHolderSignalsCalculator();
        for (var filer = 0; filer < StockHolderSignalsCalculator.MinimumRankedFilers; filer++)
            calculator.Add(
                Holder(
                    3_000,
                    filer == 0 ? [new(broad, 10, 100)] : [new(thin, 50, 100), new(broad, 10, 100)],
                    turnover: HolderTurnoverBand.High
                )
            );

        var rows = calculator
            .Build(
                Quarter,
                Counts(
                    (thin, 1_000, Quarter, SharesOutstandingBasis.Consolidated),
                    (broad, 1_000, Quarter, SharesOutstandingBasis.Consolidated)
                )
            )
            .ToDictionary(row => row.EquityIssuerId);

        var thinRow = rows[thin.IssuerId];
        thinRow.Filers.Should().Be(StockHolderSignalsCalculator.MinimumRankedFilers - 1);
        StockHolderSignalsMath.Crowding(thinRow).Should().Be(0.45m, "the value is still reported");
        StockHolderSignalsMath.ConvictionShare(thinRow).Should().Be(1m);
        thinRow.CrowdingPercentile.Should().BeNull();
        StockHolderSignalsMath.RankedCrowding(thinRow).Should().BeNull();
        StockHolderSignalsMath.RankedConvictionShare(thinRow).Should().BeNull();
        StockHolderSignalsMath.NotRankedReason(thinRow).Should().Contain("fewer than 10");
        rows[broad.IssuerId].CrowdingPercentile.Should().Be(100m, "it ranks alone");
        StockHolderSignalsMath.RankedCrowding(rows[broad.IssuerId]).Should().Be(0.1m);
        StockHolderSignalsMath.NotRankedReason(rows[broad.IssuerId]).Should().BeNull();
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

    private static StockHolderSignalsQuarterInputs Counts(
        params (HolderSecurityKey Security, long Count, DateOnly AsOf, SharesOutstandingBasis Basis)[] counts
    ) =>
        new()
        {
            SharesOutstanding = counts.ToDictionary(
                count => count.Security.IssuerId,
                count => (count.Count, count.AsOf, count.Basis)
            ),
        };

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
