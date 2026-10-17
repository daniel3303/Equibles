using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderTurnoverCalculatorTests
{
    private static readonly HolderSecurityKey Increased = new(Guid.NewGuid(), null);
    private static readonly HolderSecurityKey Opened = new(Guid.NewGuid(), null);
    private static readonly HolderSecurityKey Exited = new(Guid.NewGuid(), null);
    private static readonly HolderSecurityKey Reduced = new(Guid.NewGuid(), null);

    [Fact]
    public void Trades_PricesByAverageThenQuarterEndThenThePriorQuarterForAnExit()
    {
        HolderEquityPosition[] previous =
        [
            new(Increased, 100, 1_000),
            new(Exited, 40, 400),
            new(Reduced, 30, 300),
        ];
        HolderEquityPosition[] current =
        [
            new(Increased, 150, 1_800),
            new(Opened, 10, 200),
            new(Reduced, 20, 300),
        ];

        var trades = HolderTurnoverCalculator.Trades(
            current,
            previous,
            new Dictionary<HolderSecurityKey, decimal> { [Increased] = 11m }
        );

        // Buys: 50 at the 11 average plus 10 at the 20 quarter-end price. Sells: the exit at
        // the prior quarter's 10 and 10 shares at the current 15.
        trades.EquityAum.Should().Be(2_300);
        trades.PreviousEquityAum.Should().Be(1_700);
        trades.BuyValue.Should().Be(750m);
        trades.SellValue.Should().Be(550m);
        trades.Turnover.Should().Be(550m / 2_000m);
        trades.NewCount.Should().Be(1);
        trades.ExitCount.Should().Be(1);
        trades.IncreasedCount.Should().Be(1);
        trades.ReducedCount.Should().Be(1);
        trades.UnpricedTradeCount.Should().Be(0);
    }

    [Fact]
    public void Trades_SiblingListingsOfOneIssuerNeverNet()
    {
        var issuer = Guid.NewGuid();
        var classA = new HolderSecurityKey(issuer, null);
        var classB = new HolderSecurityKey(issuer, "ACME.B");

        var trades = HolderTurnoverCalculator.Trades(
            [new(classB, 100, 1_000)],
            [new(classA, 100, 1_000)]
        );

        trades.ExitCount.Should().Be(1);
        trades.NewCount.Should().Be(1);
        trades.BuyValue.Should().Be(1_000m);
        trades.SellValue.Should().Be(1_000m);
        trades.Turnover.Should().Be(1m);
    }

    [Fact]
    public void Trades_NoFilingTheQuarterBefore_LeavesTradesUnknown()
    {
        var trades = HolderTurnoverCalculator.Trades([new(Opened, 10, 200)], null);

        trades.EquityAum.Should().Be(200);
        trades.PreviousEquityAum.Should().BeNull();
        trades.Turnover.Should().BeNull();
        trades.NewCount.Should().BeNull();
        trades.BuyValue.Should().BeNull();
    }

    [Fact]
    public void Trades_PendingValueKeepsTheSharesButNotTheWeight()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 100, 1_000), new(Opened, 10, null)],
            [new(Increased, 100, 1_000)]
        );

        trades.EquityAum.Should().Be(1_000);
        trades.NewCount.Should().Be(1);
        trades.UnpricedTradeCount.Should().Be(1);
        trades.BuyValue.Should().Be(0m);
        trades.Turnover.Should().Be(0m);
    }

    [Fact]
    public void Trades_NoValuedBookOnEitherSide_HasNoTurnover()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 100, null)],
            [new(Increased, 50, null)]
        );

        trades.IncreasedCount.Should().Be(1);
        trades.Turnover.Should().BeNull();
    }

    [Fact]
    public void Trades_ZeroShareRowsAreNotPositions()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 100, 1_000), new(Exited, 0, 500)],
            [new(Increased, 100, 1_000), new(Reduced, 0, 700)]
        );

        trades.EquityAum.Should().Be(1_000);
        trades.PreviousEquityAum.Should().Be(1_000);
        trades.NewCount.Should().Be(0);
        trades.ExitCount.Should().Be(0);
        trades.Turnover.Should().Be(0m);
    }

    [Fact]
    public void Trades_UnresolvedSplitBasis_LeavesAHeldSecurityUnpricedButKeepsItsValue()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 200, 2_000), new(Opened, 10, 100)],
            [new(Increased, 100, 1_000), new(Exited, 5, 50)],
            unresolvedBasis: new HashSet<HolderSecurityKey> { Increased, Opened, Exited }
        );

        trades.EquityAum.Should().Be(2_100);
        trades.IncreasedCount.Should().Be(0);
        trades.NewCount.Should().Be(1);
        trades.ExitCount.Should().Be(1);
        trades.UnpricedTradeCount.Should().Be(1);
        trades.BuyValue.Should().Be(100m);
        trades.SellValue.Should().Be(50m);
    }

    [Fact]
    public void Trades_AnIncreaseWithAPendingValueIsUnpriced()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 150, null)],
            [new(Increased, 100, 1_000)]
        );

        trades.IncreasedCount.Should().Be(1);
        trades.UnpricedTradeCount.Should().Be(1);
        trades.BuyValue.Should().Be(0m);
    }

    [Fact]
    public void Trades_AnAveragePriceAlsoPricesAnExit()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 100, 1_000)],
            [new(Increased, 100, 1_000), new(Exited, 40, 400)],
            new Dictionary<HolderSecurityKey, decimal> { [Exited] = 12m }
        );

        trades.ExitCount.Should().Be(1);
        trades.SellValue.Should().Be(480m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Trades_AnAveragePriceOfZeroOrLessIsMissing(double average)
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 150, 1_800)],
            [new(Increased, 100, 1_000)],
            new Dictionary<HolderSecurityKey, decimal> { [Increased] = (decimal)average }
        );

        trades.BuyValue.Should().Be(600m);
        trades.UnpricedTradeCount.Should().Be(0);
    }

    [Fact]
    public void Trades_ANegativeFiledValueCarriesNoWeight()
    {
        var trades = HolderTurnoverCalculator.Trades(
            [new(Increased, 100, 1_000), new(Opened, 10, -50)],
            [new(Increased, 100, 1_000)]
        );

        trades.EquityAum.Should().Be(1_000);
    }

    [Fact]
    public void TrailingAnnual_NeedsFourKnownQuarters()
    {
        HolderTurnoverCalculator.TrailingAnnual([0.1m, 0.2m, 0.3m, 0.4m]).Should().Be(1.0m);
        HolderTurnoverCalculator.TrailingAnnual([0.1m, 0.2m, 0.3m]).Should().BeNull();
        HolderTurnoverCalculator.TrailingAnnual([0.1m, null, 0.3m, 0.4m]).Should().BeNull();
    }
}
