using Equibles.DelayedTrades.BusinessLogic.Prints;
using Equibles.DelayedTrades.BusinessLogic.Sessions;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: a session bar keys on ISIN, venue and the LOCAL date of the trade time; open and close are
/// the first and last price-forming prints by trade time, so the same-priced closing-auction cluster
/// sets the close whatever order it was published in; volume counts every counted print, lit volume
/// only the price-forming ones; a key with no price-forming print carries volume but no bar.
/// </summary>
public class DelayedTradeSessionAggregatorTests
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Lisbon"
    );

    private static List<DelayedTradePrint> LisbonPrints(bool countedOnly = true)
    {
        var all = EuronextTradesFileParser
            .Read(TradesFixture.Zip(TradesFixture.Csv(TradesFixture.LisbonExcerpt)), new())
            .ToList();
        var ledger = DelayedTradeModificationLedger.Build(all);
        return all.Where(print => ledger.IsEffective(print))
            .Where(print => !countedOnly || DelayedTradePrintFilter.IsCounted(print))
            .Where(print => print.Currency == "EUR")
            .ToList();
    }

    [Fact]
    public void LisbonExcerpt_DerivesTheSessionBars()
    {
        var result = DelayedTradeSessionAggregator.Aggregate(LisbonPrints(), Lisbon);

        var pharol = result.Bars.Single(bar =>
            bar.Isin == "PTPTC0AM0009"
            && bar.Venue == "XLIS"
            && bar.SessionDate == new DateOnly(2026, 9, 15)
        );
        pharol.HasBar.Should().BeTrue();
        pharol.Open.Should().Be(0.0891m);
        pharol.High.Should().Be(0.0899m, "the cancelled 0.50 outlier never counts");
        pharol.Low.Should().Be(0.0890m);
        pharol
            .Close.Should()
            .Be(
                0.0890m,
                "the closing-auction cluster at 15:35:20Z is the last price-forming print"
            );
        pharol.Volume.Should().Be(677_437L);
        pharol.LitVolume.Should().Be(677_437L);
        pharol.PrintCount.Should().Be(51);
        pharol
            .LastPriceForming.TradedAtUtc.Should()
            .Be(new DateTime(2026, 9, 15, 15, 35, 20, 560, DateTimeKind.Utc).AddTicks(6220));

        var edp = result.Bars.Single(bar => bar.Isin == "PTEDP0AM0009");
        edp.PrintCount.Should().Be(5);
        edp.DarkPrintCount.Should().Be(3);
        edp.Volume.Should().Be(1_493L, "dark RFPT prints add volume");
        edp.LitVolume.Should().Be(5L);
        edp.Close.Should().Be(4.61m);

        var enxl = result.Bars.Single(bar => bar.Venue == "ENXL");
        enxl.Isin.Should().Be("PTRIZ0AM0009");
        enxl.Open.Should().Be(1.37m);
        enxl.Close.Should().Be(1.46m);
        enxl.Volume.Should().Be(1_863L);

        var nextDay = result.Bars.Single(bar => bar.SessionDate == new DateOnly(2026, 9, 16));
        nextDay.Isin.Should().Be("PTPTC0AM0009");
        nextDay.HasBar.Should().BeFalse("an off-book print alone forms no price");
        nextDay.Volume.Should().Be(10L);

        result.PrintCount.Should().Be(62);
        result.LitPrintCount.Should().Be(58);
        result.DarkPrintCount.Should().Be(3);
        result.DuplicateCount.Should().Be(0);
    }

    [Fact]
    public void DuplicateDetection_CountsARepeatedIdentifierOnce()
    {
        var result = DelayedTradeSessionAggregator.Aggregate(
            LisbonPrints(),
            Lisbon,
            detectDuplicateTradeIds: true
        );

        result.DuplicateCount.Should().Be(1);
        result
            .Bars.Single(bar =>
                bar.Isin == "PTPTC0AM0009" && bar.SessionDate == new DateOnly(2026, 9, 15)
            )
            .Volume.Should()
            .Be(677_430L);
    }

    [Fact]
    public void OutOfOrderPublication_StillOrdersByTradeTime()
    {
        var late = DelayedTradePrintFilterTests.Print(
            price: 10m,
            tradedAt: Utc(9, 0),
            publishedAt: Utc(9, 30),
            tradeId: "A",
            line: 3
        );
        var early = DelayedTradePrintFilterTests.Print(
            price: 12m,
            tradedAt: Utc(9, 5),
            publishedAt: Utc(9, 6),
            tradeId: "B",
            line: 4
        );
        var last = DelayedTradePrintFilterTests.Print(
            price: 11m,
            tradedAt: Utc(15, 35),
            publishedAt: Utc(15, 35),
            tradeId: "C",
            line: 5
        );

        var bar = DelayedTradeSessionAggregator
            .Aggregate([last, early, late], Lisbon)
            .Bars.Single();

        bar.Open.Should().Be(10m);
        bar.Close.Should().Be(11m);
        bar.High.Should().Be(12m);
        bar.Low.Should().Be(10m);
    }

    [Fact]
    public void SameTradeTime_TieBreaksOnPublicationThenLine()
    {
        var a = DelayedTradePrintFilterTests.Print(
            price: 1m,
            tradedAt: Utc(15, 35),
            publishedAt: Utc(15, 35),
            tradeId: "A",
            line: 9
        );
        var b = DelayedTradePrintFilterTests.Print(
            price: 2m,
            tradedAt: Utc(15, 35),
            publishedAt: Utc(15, 35),
            tradeId: "B",
            line: 10
        );
        var c = DelayedTradePrintFilterTests.Print(
            price: 3m,
            tradedAt: Utc(15, 35),
            publishedAt: Utc(15, 36),
            tradeId: "C",
            line: 4
        );

        var bar = DelayedTradeSessionAggregator.Aggregate([c, b, a], Lisbon).Bars.Single();

        bar.Open.Should().Be(1m);
        bar.Close.Should().Be(3m);
    }

    [Fact]
    public void TheLocalDate_DecidesTheSession_AcrossTheDstChange()
    {
        // Europe/Lisbon leaves summer time on 2026-10-25 at 01:00 UTC.
        var summer = DelayedTradePrintFilterTests.Print(
            tradedAt: new DateTime(2026, 10, 24, 23, 30, 0, DateTimeKind.Utc),
            tradeId: "S"
        );
        var winter = DelayedTradePrintFilterTests.Print(
            tradedAt: new DateTime(2026, 10, 26, 23, 30, 0, DateTimeKind.Utc),
            tradeId: "W"
        );

        DelayedTradeSessionAggregator
            .SessionDate(summer.TradedAtUtc, Lisbon)
            .Should()
            .Be(new DateOnly(2026, 10, 25), "23:30Z is 00:30 WEST");
        DelayedTradeSessionAggregator
            .SessionDate(winter.TradedAtUtc, Lisbon)
            .Should()
            .Be(new DateOnly(2026, 10, 26), "23:30Z is 23:30 WET");
    }

    [Fact]
    public void Prices_RoundToTheStoredPrecision_AndVolumeFloors()
    {
        var print = DelayedTradePrintFilterTests.Print(price: 1.23456m, quantity: 10.9m);
        var bar = DelayedTradeSessionAggregator.Aggregate([print], Lisbon).Bars.Single();
        bar.Close.Should().Be(1.2346m);
        bar.Volume.Should().Be(10L);
    }

    private static DateTime Utc(int hour, int minute) =>
        new(2026, 9, 15, hour, minute, 0, DateTimeKind.Utc);
}
