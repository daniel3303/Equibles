using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: the parser streams the one CSV entry of the zip, insists on the venue's notice line and
/// its 20-column header, reads every flag verbatim, counts a malformed row instead of failing the
/// file, and refuses a file whose shape changed.
/// </summary>
public class EuronextTradesFileParserTests
{
    [Fact]
    public void LisbonExcerpt_ReadsEveryWellFormedRowWithItsFlags()
    {
        var counters = new DelayedTradeParseCounters();
        var prints = EuronextTradesFileParser
            .Read(TradesFixture.Zip(TradesFixture.Csv(TradesFixture.LisbonExcerpt)), counters)
            .ToList();

        counters.Rows.Should().Be(69);
        counters.MalformedRows.Should().Be(1, "one synthetic row has 19 columns");
        prints.Should().HaveCount(68);

        var first = prints.First(print => print.TradeId == "1OIACAXBW");
        first.Isin.Should().Be("PTPTC0AM0009");
        first.Venue.Should().Be("XLIS");
        first
            .TradedAtUtc.Should()
            .Be(new DateTime(2026, 9, 15, 7, 41, 9, 896, DateTimeKind.Utc).AddTicks(5870));
        first.TradedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        first.Price.Should().Be(0.0891m);
        first.Quantity.Should().Be(5000m);
        first.Currency.Should().Be("EUR");
        first.PriceNotation.Should().Be("MONE");
        first.MarketMechanism.Should().Be("1");
        first.Modification.Should().Be(DelayedTradeModification.None);
        first.MissingPrice.Should().BeFalse();
        first.LineNumber.Should().Be(5, "two EDP prints precede it in the excerpt");

        prints
            .Single(print =>
                print.TradeId == "SYNTH-AMND-1"
                && print.Modification == DelayedTradeModification.Amendment
            )
            .Price.Should()
            .Be(0.0892m);
        prints
            .Single(print =>
                print.TradeId == "SYNTH-CANC-1"
                && print.Modification == DelayedTradeModification.Cancellation
            )
            .Quantity.Should()
            .Be(1_000_000m);
        prints.Single(print => print.TradeId == "SYNTH-MISS-1").MissingPrice.Should().BeTrue();
        prints.Single(print => print.TradeId == "SYNTH-PERC-1").PriceNotation.Should().Be("PERC");
        prints
            .Single(print => print.TradeId == "SYNTH-SCI-1")
            .Quantity.Should()
            .Be(15m, "quantities may be written in scientific notation");
        prints
            .Where(print => print.BenchmarkIndicator == "RFPT")
            .Should()
            .HaveCount(3)
            .And.OnlyContain(print => print.MarketMechanism == "3");
        prints.Where(print => print.Venue == "ENXL").Should().HaveCount(5);
    }

    [Fact]
    public void Parse_IsReEnumerable_SoTwoPassesReadTheSameRows()
    {
        var source = new EuronextDelayedTradeSource(new HttpClient());
        var file = TradesFixture.Lisbon();

        var first = source.Parse(file, new DelayedTradeParseCounters()).Count();
        var second = source.Parse(file, new DelayedTradeParseCounters()).Count();

        first.Should().Be(68);
        second.Should().Be(first);
    }

    [Fact]
    public void HeaderOnlyFile_YieldsNothingAndCountsNothing()
    {
        var counters = new DelayedTradeParseCounters();
        EuronextTradesFileParser
            .Read(TradesFixture.Zip(TradesFixture.Csv(TradesFixture.HeaderOnly)), counters)
            .Should()
            .BeEmpty();
        counters.Rows.Should().Be(0);
    }

    [Fact]
    public void AFileWithoutTheNoticeLine_IsRefused()
    {
        var csv = string.Join(
            '\n',
            TradesFixture.Csv(TradesFixture.HeaderOnly).Split('\n').Skip(1)
        );
        var act = () => EuronextTradesFileParser.Read(TradesFixture.Zip(csv), new()).ToList();
        act.Should().Throw<InvalidDataException>().WithMessage("*terms notice*");
    }

    [Fact]
    public void AChangedHeader_IsRefused()
    {
        var csv = TradesFixture.Csv(TradesFixture.HeaderOnly).Replace("MifidPrice", "Price");
        var act = () => EuronextTradesFileParser.Read(TradesFixture.Zip(csv), new()).ToList();
        act.Should().Throw<InvalidDataException>().WithMessage("*header*");
    }

    [Fact]
    public void AZipWithAnotherEntryName_IsRefused()
    {
        var zip = TradesFixture.Zip(
            TradesFixture.Csv(TradesFixture.HeaderOnly),
            "Trades_Bonds.csv"
        );
        var act = () => EuronextTradesFileParser.Read(zip, new()).ToList();
        act.Should().Throw<InvalidDataException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void ATimestampWithAnOffset_IsMalformed()
    {
        // The feed states UTC with a Z suffix; a zoned token would silently shift the session date.
        var header = TradesFixture.Csv(TradesFixture.HeaderOnly);
        var row = TradesFixture.Csv(TradesFixture.LisbonExcerpt).Split('\n')[2];
        var zoned = row.Replace("Z\"", "+01:00\"");
        zoned.Should().NotBe(row);
        var counters = new DelayedTradeParseCounters();

        var prints = EuronextTradesFileParser
            .Read(TradesFixture.Zip(header + row + "\n" + zoned + "\n"), counters)
            .ToList();

        prints.Should().ContainSingle();
        counters.MalformedRows.Should().Be(1);
    }

    [Fact]
    public void MoreThanTheMalformedAllowance_FailsTheFile()
    {
        var header = TradesFixture.Csv(TradesFixture.HeaderOnly);
        var csv =
            header
            + string.Concat(
                Enumerable.Repeat("\"broken\"\n", EuronextTradesFileParser.MaxMalformedRows + 1)
            );
        var act = () => EuronextTradesFileParser.Read(TradesFixture.Zip(csv), new()).ToList();
        act.Should().Throw<InvalidDataException>().WithMessage("*malformed*");
    }

    [Theory]
    [InlineData("\"a\",\"b\",\"c\"", 3, new[] { "a", "b", "c" })]
    [InlineData("\"a,1\",\"say \"\"hi\"\"\",\"\"", 3, new[] { "a,1", "say \"hi\"", "" })]
    [InlineData("a,b,c", 3, new[] { "a", "b", "c" })]
    public void Csv_SplitsQuotedFields(string line, int columns, string[] expected)
    {
        EuronextTradesCsv.Split(line, columns).Should().Equal(expected);
    }

    [Theory]
    [InlineData("\"a\",\"b\"", 3)]
    [InlineData("\"unterminated,\"b\",\"c\"", 3)]
    public void Csv_RefusesTheWrongShape(string line, int columns)
    {
        EuronextTradesCsv.Split(line, columns).Should().BeNull();
    }

    [Fact]
    public void ReadRow_RefusesAnUnknownModificationToken()
    {
        var line =
            "\"2026-09-15T07:41:09.896587Z\",\"2026-09-15T07:41:09.896694Z\",\"PTPTC0AM0009\",\"0.0891000\",\"5000.0\",\"MONE\",\"EUR\",\"1\",\"-\",\"NEWT\",\"-\",\"-\",\"-\",\"-\",\"XLIS\",\"\",\"X\",\"\",\"-\",\"XLIS\"";
        EuronextTradesFileParser.ReadRow(line, 3).Should().BeNull();
    }
}
