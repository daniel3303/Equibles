using Equibles.DelayedTrades.BusinessLogic;
using Equibles.DelayedTrades.BusinessLogic.Bars;
using Equibles.DelayedTrades.BusinessLogic.Configuration;
using Equibles.DelayedTrades.BusinessLogic.Import;
using Equibles.DelayedTrades.Data.Models;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: a settle pass reads the session date from the file, writes one bar per matched verified
/// listing, records the capture and marks the partition once; a second pass is a no-op unless it
/// re-derives; a file matching nothing refuses the marker; an intraday poll upserts the latest
/// delayed print per listing, drops prints younger than the delay and never regresses a session.
/// </summary>
public class DelayedTradeImportServiceTests
{
    private static readonly EquityMarket Lisbon = EquityMarketCatalog.TryGet("euronext-lisbon");
    private static readonly DateTime SettleNow = new(2026, 9, 16, 3, 54, 32, DateTimeKind.Utc);

    private sealed class FixtureSource(DelayedTradeFile file) : IDelayedTradeSource
    {
        private readonly EuronextDelayedTradeSource _inner = new(new HttpClient());
        public List<(string Location, DelayedTradeWindow Window)> Fetches { get; } = [];
        public int Parses { get; private set; }
        public string SourceKey => EuronextDelayedTradeTerms.SourceKey;
        public DelayedTradeAttribution Attribution => EuronextDelayedTradeTerms.Attribution;

        public Task<DelayedTradeFile> Fetch(
            string locationCode,
            DelayedTradeWindow window,
            CancellationToken cancellationToken
        )
        {
            Fetches.Add((locationCode, window));
            return Task.FromResult(file with { Window = window });
        }

        public IEnumerable<DelayedTradePrint> Parse(
            DelayedTradeFile served,
            DelayedTradeParseCounters counters
        )
        {
            Parses++;
            return _inner.Parse(served, counters);
        }
    }

    private static DelayedTradeImportService Service(
        DelayedTradeDbFixture db,
        DelayedTradeScraperOptions options = null
    ) =>
        new(
            db.ScopeFactory,
            new DelayedTradeBarWriter(db.ScopeFactory, NullLogger<DelayedTradeBarWriter>.Instance),
            Microsoft.Extensions.Options.Options.Create(
                options ?? new DelayedTradeScraperOptions()
            ),
            NullLogger<DelayedTradeImportService>.Instance
        );

    private static DelayedTradeFile NoSession() =>
        new(
            "euronext",
            "LIS",
            DelayedTradeWindow.CurrentSession,
            DelayedTradeFetchOutcome.NoSession,
            EuronextDelayedTradeSource.FileUrl("LIS", DelayedTradeWindow.CurrentSession).ToString(),
            EuronextDelayedTradeTerms.TermsUrl,
            null,
            0,
            SettleNow,
            null
        );

    [Fact]
    public async Task Settle_WritesTheMatchedBars_MarksTheSession_AndIsIdempotent()
    {
        var db = new DelayedTradeDbFixture();
        var pharol = db.SeedListing("PHR", "PTPTC0AM0009");
        var edp = db.SeedListing("EDP", "PTEDP0AM0009");
        db.SeedListing("RIZ", "PTRIZ0AM0009", mic: "ENXL");
        var source = new FixtureSource(TradesFixture.Lisbon());
        var service = Service(db);

        var first = await service.SettleSession(
            Lisbon,
            source,
            rederive: false,
            SettleNow,
            CancellationToken.None
        );

        first.Outcome.Should().Be(DelayedTradeSettleOutcome.Imported);
        first.SessionDate.Should().Be(new DateOnly(2026, 9, 15));
        source
            .Fetches.Should()
            .ContainSingle()
            .Which.Should()
            .Be(("LIS", DelayedTradeWindow.PreviousSession));
        var bar = db.Prices(db.ListingOf(pharol).Id).Single();
        bar.Date.Should().Be(new DateOnly(2026, 9, 15));
        bar.Close.Should().Be(0.0890m);
        bar.Volume.Should().Be(677_437L);
        bar.SourceTicker.Should().Be("XLIS:PTPTC0AM0009");
        var edpBar = db.Prices(db.ListingOf(edp).Id).Single();
        edpBar.Close.Should().Be(4.61m);
        edpBar.Volume.Should().Be(1_493L, "the three RFPT dark prints add volume");

        var partition = first.Partition;
        partition.Dataset.Should().Be(DelayedTradeDataset.SettledBars);
        partition.ScopeKey.Should().Be("euronext-lisbon");
        partition.MatchedCount.Should().Be(3);
        partition.BarsInserted.Should().Be(3);
        partition.UnmatchedCount.Should().Be(0);
        partition.CurrencyMismatchCount.Should().Be(1, "the synthetic USD print");
        partition.CancelledCount.Should().Be(1);
        partition.AmendedCount.Should().Be(1);
        partition.OutOfSessionCount.Should().Be(1, "the off-book print after midnight local");
        partition.DarkPrintCount.Should().Be(3);
        partition.FileSha256.Should().NotBeNullOrEmpty();
        partition.TermsUrl.Should().Be(EuronextDelayedTradeTerms.TermsUrl);

        using (var context = db.NewContext())
        {
            context.Set<DelayedTradeImportPartition>().Should().ContainSingle();
            var capture = context.Set<DelayedTradeFileCapture>().Single();
            capture.SessionDate.Should().Be(new DateOnly(2026, 9, 15));
            capture.Rows.Should().Be(69);
            capture.Outcome.Should().Be(DelayedTradeFetchOutcome.Served);
        }

        var parsesAfterFirst = source.Parses;
        var second = await service.SettleSession(
            Lisbon,
            source,
            rederive: false,
            SettleNow.AddHours(1),
            CancellationToken.None
        );
        second.Outcome.Should().Be(DelayedTradeSettleOutcome.AlreadyImported);
        second.SessionDate.Should().Be(new DateOnly(2026, 9, 15));
        second.Partition.FileSha256.Should().Be(partition.FileSha256);
        source
            .Parses.Should()
            .Be(parsesAfterFirst, "the same file is recognised by its hash before any parse");
        db.Prices(db.ListingOf(pharol).Id).Should().ContainSingle();
        using (var context = db.NewContext())
        {
            var captures = context
                .Set<DelayedTradeFileCapture>()
                .OrderBy(row => row.FetchedAtUtc)
                .ToList();
            captures.Should().HaveCount(2);
            captures[1]
                .Rows.Should()
                .Be(69, "the ledger carries the row count of the file it already parsed");
            captures[1].SessionDate.Should().Be(new DateOnly(2026, 9, 15));
        }

        var third = await service.SettleSession(
            Lisbon,
            source,
            rederive: true,
            SettleNow.AddHours(9),
            CancellationToken.None
        );
        third.Outcome.Should().Be(DelayedTradeSettleOutcome.Rederived);
        source
            .Parses.Should()
            .BeGreaterThan(parsesAfterFirst, "a recheck re-derives even an unchanged file");
        third.Partition.RederivedCount.Should().Be(1);
        third.Partition.BarsInserted.Should().Be(0);
        using (var context = db.NewContext())
            context.Set<DelayedTradeImportPartition>().Should().ContainSingle();
    }

    [Fact]
    public async Task Settle_WithNoMatchedListing_RefusesTheMarker()
    {
        var db = new DelayedTradeDbFixture();
        var service = Service(db);

        var result = await service.SettleSession(
            Lisbon,
            new FixtureSource(TradesFixture.Lisbon()),
            rederive: false,
            SettleNow,
            CancellationToken.None
        );

        result.Outcome.Should().Be(DelayedTradeSettleOutcome.Refused);
        using var context = db.NewContext();
        context.Set<DelayedTradeImportPartition>().Should().BeEmpty();
        context
            .Set<DelayedTradeFileCapture>()
            .Should()
            .ContainSingle("the fetch is still evidence");
    }

    [Fact]
    public async Task Settle_BeforeTheSessionIsSettledLocally_WritesNothingAndRefuses()
    {
        var db = new DelayedTradeDbFixture();
        var pharol = db.SeedListing("PHR", "PTPTC0AM0009");
        var service = Service(db);

        // 22:00 UTC on the session day is 23:00 in Lisbon: the local date has not rolled.
        var result = await service.SettleSession(
            Lisbon,
            new FixtureSource(TradesFixture.Lisbon()),
            rederive: false,
            new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc),
            CancellationToken.None
        );

        result
            .Outcome.Should()
            .Be(
                DelayedTradeSettleOutcome.Refused,
                "a session no bar of which landed is not settled"
            );
        result.Refusal.Should().StartWith("no bar landed: 1 unsettled");
        result.Partition.BarsUnsettled.Should().Be(1);
        db.Prices(db.ListingOf(pharol).Id).Should().BeEmpty();
        using var context = db.NewContext();
        context
            .Set<DelayedTradeImportPartition>()
            .Should()
            .BeEmpty("the next attempt retries the whole session");
    }

    [Fact]
    public async Task NoSession_RecordsTheCaptureOnly()
    {
        var db = new DelayedTradeDbFixture();
        db.SeedListing("PHR", "PTPTC0AM0009");
        var service = Service(db);

        var settle = await service.SettleSession(
            Lisbon,
            new FixtureSource(NoSession()),
            rederive: false,
            SettleNow,
            CancellationToken.None
        );
        var poll = await service.PollIntraday(
            Lisbon,
            new FixtureSource(NoSession()),
            SettleNow,
            CancellationToken.None
        );

        settle.Outcome.Should().Be(DelayedTradeSettleOutcome.NoSession);
        poll.Outcome.Should().Be(DelayedTradeIntradayOutcome.NoSession);
        using var context = db.NewContext();
        context
            .Set<DelayedTradeFileCapture>()
            .Should()
            .HaveCount(2)
            .And.OnlyContain(row => row.Outcome == DelayedTradeFetchOutcome.NoSession);
        context.Set<LatestDelayedTrade>().Should().BeEmpty();
    }

    [Fact]
    public async Task Intraday_UpsertsTheLatestPrint_DropsFreshPrints_AndNeverRegresses()
    {
        var db = new DelayedTradeDbFixture();
        var pharol = db.SeedListing("PHR", "PTPTC0AM0009");
        var service = Service(db);
        // Fetched at 15:45Z on the session day, so the 15:35:20Z auction cluster is younger than the 15-minute delay.
        var midSession = new DateTime(2026, 9, 15, 15, 45, 0, DateTimeKind.Utc);
        var source = new FixtureSource(
            TradesFixture.Lisbon(DelayedTradeWindow.CurrentSession, midSession)
        );

        var result = await service.PollIntraday(Lisbon, source, midSession, CancellationToken.None);

        result.Outcome.Should().Be(DelayedTradeIntradayOutcome.Updated);
        result.DroppedAsFresh.Should().BeGreaterThan(0);
        source.Fetches.Single().Window.Should().Be(DelayedTradeWindow.CurrentSession);
        LatestDelayedTrade row;
        using (var context = db.NewContext())
            row = context.Set<LatestDelayedTrade>().Single();
        row.EquityListingId.Should().Be(db.ListingOf(pharol).Id);
        row.SessionDate.Should().Be(new DateOnly(2026, 9, 15));
        row.LastTradedAtUtc.Should().BeBefore(midSession.AddMinutes(-15));
        row.IsSessionComplete.Should().BeFalse();
        row.SourceKey.Should().Be("euronext");
        row.MarketCode.Should().Be("euronext-lisbon");
        row.Currency.Should().Be("EUR");
        row.TermsUrl.Should().Be(EuronextDelayedTradeTerms.TermsUrl);
        row.SessionHigh.Should().Be(0.0899m);
        var partialVolume = row.Volume;

        // After the auction the whole session is older than the delay and the row completes.
        var afterClose = new DateTime(2026, 9, 15, 16, 10, 0, DateTimeKind.Utc);
        await service.PollIntraday(
            Lisbon,
            new FixtureSource(TradesFixture.Lisbon(DelayedTradeWindow.CurrentSession, afterClose)),
            afterClose,
            CancellationToken.None
        );
        using (var context = db.NewContext())
            row = context.Set<LatestDelayedTrade>().Single();
        row.IsSessionComplete.Should().BeTrue();
        row.LastPrice.Should().Be(0.0890m);
        row.Volume.Should().BeGreaterThan(partialVolume);
        row.LastTradedAtUtc.Should()
            .Be(new DateTime(2026, 9, 15, 15, 35, 20, 560, DateTimeKind.Utc).AddTicks(6220));

        // A stored newer session is never overwritten by an older file.
        using (var context = db.NewContext())
        {
            var stored = context.Set<LatestDelayedTrade>().Single();
            stored.SessionDate = new DateOnly(2026, 9, 17);
            stored.LastPrice = 1m;
            context.SaveChanges();
        }
        await service.PollIntraday(
            Lisbon,
            new FixtureSource(TradesFixture.Lisbon(DelayedTradeWindow.CurrentSession, afterClose)),
            afterClose,
            CancellationToken.None
        );
        using (var context = db.NewContext())
            context.Set<LatestDelayedTrade>().Single().LastPrice.Should().Be(1m);
    }

    [Fact]
    public async Task PruneCaptures_DropsOnlyRowsPastRetention()
    {
        var db = new DelayedTradeDbFixture();
        db.SeedListing("PHR", "PTPTC0AM0009");
        var service = Service(db, new DelayedTradeScraperOptions { CaptureRetentionDays = 90 });
        await service.SettleSession(
            Lisbon,
            new FixtureSource(NoSession()),
            rederive: false,
            SettleNow,
            CancellationToken.None
        );

        (await service.PruneCaptures(SettleNow.AddDays(89), CancellationToken.None)).Should().Be(0);
        (await service.PruneCaptures(SettleNow.AddDays(91), CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public void SessionDateOf_IsTheDateMostPriceFormingPrintsFallOn()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
        var prints = new[]
        {
            DelayedTradePrintFilterTests.Print(
                tradedAt: new DateTime(2026, 9, 15, 10, 0, 0, DateTimeKind.Utc),
                tradeId: "A"
            ),
            DelayedTradePrintFilterTests.Print(
                tradedAt: new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc),
                tradeId: "B"
            ),
            DelayedTradePrintFilterTests.Print(
                tradedAt: new DateTime(2026, 9, 14, 11, 0, 0, DateTimeKind.Utc),
                tradeId: "C"
            ),
        };
        var aggregation =
            Equibles.DelayedTrades.BusinessLogic.Sessions.DelayedTradeSessionAggregator.Aggregate(
                prints,
                zone
            );
        DelayedTradeImportService.SessionDateOf(aggregation).Should().Be(new DateOnly(2026, 9, 15));
    }
}
