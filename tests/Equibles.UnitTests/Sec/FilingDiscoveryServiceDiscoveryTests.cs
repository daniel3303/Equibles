using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the event-driven discovery contract: the realtime feed only dirties
/// tracked filers of synced forms and never re-dirties an already-seen entry;
/// the daily-index watermark initializes without replaying history, advances
/// only past cleanly fetched days, holds on a fetch failure (the #1264
/// lesson — a skipped day is silently lost filings), and re-checks the
/// client's prefix-matched rows with an exact form comparison so "4" cannot
/// dirty a 424B2 filer.
/// </summary>
// Shares FilingDiscoveryService's cross-cycle statics with the pending-accession
// suite; serialize them so one class's reset can't land mid-test in the other.
[Collection("SecFilingDiscoveryStatics")]
public class FilingDiscoveryServiceDiscoveryTests
{
    private static readonly DateOnly LatestFinalDay = FilingDiscoveryService.LatestFinalIndexDay(
        DateTime.UtcNow
    );

    private sealed class BackfillStateOnlyModuleConfiguration : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<BackfillState>();
        }
    }

    private static EquiblesFinancialDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[] { new BackfillStateOnlyModuleConfiguration() }
        );

    private static FilingDiscoveryService CreateService(
        ISecEdgarClient secEdgarClient,
        EquiblesFinancialDbContext context,
        DocumentScraperOptions options = null,
        params IRecentFilingSink[] sinks
    ) =>
        new(
            secEdgarClient,
            new BackfillStateRepository(context),
            Options.Create(options ?? new DocumentScraperOptions()),
            Substitute.For<ILogger<FilingDiscoveryService>>(),
            sinks
        );

    private static EquityIssuer Tracked(string ticker, string cik) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(Id: Guid.NewGuid(), Ticker: ticker, Cik: cik);

    private static ISecEdgarClient ClientWithFeed(params EdgarRecentFilingEntry[] entries)
    {
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetRecentFilings(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([.. entries]);
        client
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);
        return client;
    }

    public FilingDiscoveryServiceDiscoveryTests()
    {
        FilingDiscoveryService.ResetCrossCycleStateForTests();
    }

    [Fact]
    public async Task Feed_TrackedFilerOfSyncedForm_IsDirtied()
    {
        await using var context = CreateContext();
        EquityIssuer apple = Tracked("AAPL", "320193");
        var client = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0000320193",
                FormType = "8-K",
                AccessionNumber = "0000320193-26-000001",
            }
        );

        var dirty = await CreateService(client, context).DiscoverCompaniesWithNewFilings([apple]);

        dirty.Should().ContainSingle().Which.Should().BeSameAs(apple);
    }

    // A filer with no listing yet is followed for its registration statements only, so its 8-K
    // never dirties it while its amended S-1 does (SecForm.SyncsFor).
    [Fact]
    public async Task Feed_FilerWithNoListing_IsDirtiedOnlyByARegistrationStatement()
    {
        await using var context = CreateContext();
        var registrant = new EquityIssuer
        {
            Id = Guid.NewGuid(),
            Name = "Anthropic, PBC",
            Cik = "2130357",
        };
        var currentReport = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0002130357",
                FormType = "8-K",
                AccessionNumber = "0002130357-26-000001",
            }
        );

        var dirtiedByCurrentReport = await CreateService(currentReport, context)
            .DiscoverCompaniesWithNewFilings([registrant]);

        dirtiedByCurrentReport.Should().BeEmpty();

        FilingDiscoveryService.ResetCrossCycleStateForTests();
        var amendment = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0002130357",
                FormType = "S-1/A",
                AccessionNumber = "0001628280-26-074338",
            }
        );

        var dirtiedByAmendment = await CreateService(amendment, context)
            .DiscoverCompaniesWithNewFilings([registrant]);

        dirtiedByAmendment.Should().ContainSingle().Which.Should().BeSameAs(registrant);
    }

    // Another lane's forms ride the same poll: a sink is offered every entry the poll reads for
    // the first time, whatever its form or filer, and a failing sink never fails discovery.
    [Fact]
    public async Task Feed_OffersEachNewEntryToEverySinkOnce_AndSurvivesAFailingSink()
    {
        await using var context = CreateContext();
        EquityIssuer apple = Tracked("AAPL", "320193");
        var client = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0000320193",
                FormType = "8-K",
                AccessionNumber = "0000320193-26-000001",
            },
            new EdgarRecentFilingEntry
            {
                Cik = "0001167483",
                FormType = "13F-HR",
                AccessionNumber = "0000919574-26-008062",
            }
        );
        var failing = Substitute.For<IRecentFilingSink>();
        failing
            .Offer(Arg.Any<IReadOnlyList<EdgarRecentFilingEntry>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("sink down"));
        var offered = new List<string>();
        var sink = Substitute.For<IRecentFilingSink>();
        sink.Offer(Arg.Any<IReadOnlyList<EdgarRecentFilingEntry>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                offered.AddRange(
                    call.Arg<IReadOnlyList<EdgarRecentFilingEntry>>()
                        .Select(entry => entry.AccessionNumber)
                );
                return Task.CompletedTask;
            });
        var service = CreateService(
            client,
            context,
            new DocumentScraperOptions { RecentFeedPollSeconds = 0 },
            failing,
            sink
        );

        var dirty = await service.DiscoverCompaniesFromRecentFeed([apple]);
        // A second poll re-reads the same page: nothing is new, so nothing is offered again.
        await service.DiscoverCompaniesFromRecentFeed([apple]);

        dirty.Should().ContainSingle().Which.Should().BeSameAs(apple);
        offered.Should().Equal("0000320193-26-000001", "0000919574-26-008062");
    }

    [Fact]
    public async Task Feed_UntrackedCikOrUnsyncedForm_IsIgnored()
    {
        await using var context = CreateContext();
        EquityIssuer apple = Tracked("AAPL", "320193");
        var client = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0000999999",
                FormType = "8-K",
                AccessionNumber = "0000999999-26-000001",
            },
            new EdgarRecentFilingEntry
            {
                Cik = "0000320193",
                FormType = "424B2",
                AccessionNumber = "0000320193-26-000002",
            }
        );

        var dirty = await CreateService(client, context).DiscoverCompaniesWithNewFilings([apple]);

        dirty.Should().BeEmpty();
    }

    [Fact]
    public async Task Feed_AlreadySeenEntry_IsNotReDirtied()
    {
        await using var context = CreateContext();
        EquityIssuer apple = Tracked("AAPL", "320193");
        var client = ClientWithFeed(
            new EdgarRecentFilingEntry
            {
                Cik = "0000320193",
                FormType = "8-K",
                AccessionNumber = "0000320193-26-000001",
            }
        );
        // Poll throttle off so both cycles poll the feed.
        var options = new DocumentScraperOptions { RecentFeedPollSeconds = 0 };

        var service = CreateService(client, context, options);
        var firstCycle = await service.DiscoverCompaniesWithNewFilings([apple]);
        var secondCycle = await service.DiscoverCompaniesWithNewFilings([apple]);

        firstCycle.Should().ContainSingle();
        secondCycle.Should().BeEmpty();
    }

    // A fund's 485 filing listed twice on the first page ended the walk there after every restart, so
    // an 8-K on the third page was never flagged.
    [Fact]
    public async Task Feed_FilingListedTwiceOnOnePage_StillPagesOnToLaterFilings()
    {
        await using var context = CreateContext();
        EquityIssuer nvidia = Tracked("NVDA", "1045810");
        var firstPage = Enumerable
            .Range(0, 98)
            .Select(i => new EdgarRecentFilingEntry
            {
                Cik = "0000999999",
                FormType = "4",
                AccessionNumber = $"0000999999-26-{i:D6}",
            })
            .ToList();
        var fundFiling = new EdgarRecentFilingEntry
        {
            Cik = "0001810747",
            FormType = "485BXT",
            AccessionNumber = "0001829126-26-012723",
        };
        firstPage.Add(fundFiling);
        firstPage.Add(fundFiling);
        var client = ClientWithFeed();
        client.GetRecentFilings(0, 100, Arg.Any<CancellationToken>()).Returns(firstPage);
        client
            .GetRecentFilings(100, 100, Arg.Any<CancellationToken>())
            .Returns([
                new EdgarRecentFilingEntry
                {
                    Cik = "0001045810",
                    FormType = "8-K",
                    AccessionNumber = "0001045810-26-000090",
                },
            ]);

        var dirty = await CreateService(client, context).DiscoverCompaniesWithNewFilings([nvidia]);

        dirty.Should().ContainSingle().Which.Should().BeSameAs(nvidia);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly TimeSpan Eastern = TimeSpan.FromHours(-5);

    private static readonly DateTimeOffset Night = new(2026, 11, 27, 10, 50, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Release = new(2026, 11, 27, 11, 6, 0, TimeSpan.Zero);

    private static EdgarRecentFilingEntry FeedEntry(
        string cik,
        string form,
        int serial,
        DateTimeOffset accepted
    ) =>
        new()
        {
            Cik = cik,
            FormType = form,
            AccessionNumber = $"{cik}-26-{serial:D6}",
            Updated = accepted,
        };

    // Ownership forms, which EDGAR disseminates at once even after hours, one a minute back from
    // 21:56 ET on 2026-11-25.
    private static List<EdgarRecentFilingEntry> EveningForms(int first, int count) =>
        [
            .. Enumerable
                .Range(first, count)
                .Select(i =>
                    FeedEntry(
                        "0000999999",
                        "4",
                        i,
                        new DateTimeOffset(2026, 11, 25, 21, 56, 0, Eastern).AddMinutes(-i)
                    )
                ),
        ];

    private static readonly EquityIssuer AppliedDigital = Tracked("APLD", "1144879");
    private static readonly EquityIssuer VishayPrecision = Tracked("VPG", "1487952");

    // The feed as recorded around the 06:00 ET release of 2026-11-27 (EDGAR closed for
    // Thanksgiving): overnight it held the evening of 11-25 (pages 0 to 2 here reach 16:57 ET);
    // at 06:06 ET the morning's first filings and the 22 effectiveness notices stamped 00:15 ET
    // headed page 0, and the held 8-Ks sat among the evening entries further down: Applied
    // Digital (20:54 ET) on the second page, Vishay Precision (17:49 ET) on the third here.
    private static ISecEdgarClient ReleaseFeed()
    {
        var evening = EveningForms(0, 300);
        List<EdgarRecentFilingEntry> morningFirst =
        [
            .. Enumerable
                .Range(0, 5)
                .Select(i =>
                    FeedEntry(
                        "0000888888",
                        "6-K",
                        i,
                        new DateTimeOffset(2026, 11, 27, 6, 5, 0, Eastern).AddMinutes(-i)
                    )
                ),
            .. Enumerable
                .Range(0, 22)
                .Select(i =>
                    FeedEntry(
                        "0000777777",
                        "EFFECT",
                        i,
                        new DateTimeOffset(2026, 11, 27, 0, 15, 24, Eastern).AddSeconds(-i)
                    )
                ),
            .. evening.Take(73),
        ];
        List<EdgarRecentFilingEntry> morningSecond =
        [
            .. evening.Skip(73).Take(27),
            FeedEntry(
                "0001144879",
                "8-K",
                69,
                new DateTimeOffset(2026, 11, 25, 20, 54, 45, Eastern)
            ),
            .. evening.Skip(100).Take(72),
        ];
        List<EdgarRecentFilingEntry> morningThird =
        [
            .. evening.Skip(172).Take(28),
            FeedEntry(
                "0001487952",
                "8-K",
                38325,
                new DateTimeOffset(2026, 11, 25, 17, 49, 20, Eastern)
            ),
            .. evening.Skip(200).Take(71),
        ];
        var client = ClientWithFeed();
        client
            .GetRecentFilings(0, 100, Arg.Any<CancellationToken>())
            .Returns([.. evening.Take(100)], morningFirst);
        client
            .GetRecentFilings(100, 100, Arg.Any<CancellationToken>())
            .Returns([.. evening.Skip(100).Take(100)], morningSecond);
        client
            .GetRecentFilings(200, 100, Arg.Any<CancellationToken>())
            .Returns([.. evening.Skip(200)], morningThird);
        client
            .GetRecentFilings(300, 100, Arg.Any<CancellationToken>())
            .Returns([.. EveningForms(300, 100)]);
        return client;
    }

    private static FilingDiscoveryService ServiceAt(
        ISecEdgarClient client,
        EquiblesFinancialDbContext context,
        Clock clock
    ) =>
        new(
            client,
            new BackfillStateRepository(context),
            Options.Create(new DocumentScraperOptions { RecentFeedMaxPages = 15 }),
            Substitute.For<ILogger<FilingDiscoveryService>>(),
            clock: clock
        );

    // Before the sweep the walk stopped on page 0, which held entries the night's polls had read,
    // and both 8-Ks waited over four hours for a worker restart's full walk.
    [Fact]
    public async Task Feed_AfterTheMorningRelease_WalksPastReadEntriesToTheHeldFilings()
    {
        await using var context = CreateContext();
        var client = ReleaseFeed();
        var clock = new Clock(Night);
        var service = ServiceAt(client, context, clock);

        var night = await service.DiscoverCompaniesFromRecentFeed([
            AppliedDigital,
            VishayPrecision,
        ]);
        clock.Now = Release;
        var released = await service.DiscoverCompaniesFromRecentFeed([
            AppliedDigital,
            VishayPrecision,
        ]);

        night.Should().BeEmpty();
        released.Should().HaveCount(2).And.Contain(AppliedDigital).And.Contain(VishayPrecision);
    }

    // The page past 17:30 ET on the evening's day holds nothing held back, so the sweep ends there.
    [Fact]
    public async Task Feed_AfterTheMorningRelease_StopsOnThePagePastTheEveningsCutoff()
    {
        await using var context = CreateContext();
        var client = ReleaseFeed();
        var clock = new Clock(Night);
        var service = ServiceAt(client, context, clock);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        client.ClearReceivedCalls();
        clock.Now = Release;

        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);

        await client
            .Received(3)
            .GetRecentFilings(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // A sweep ten minutes after the last reads to the cutoff again; one sooner stops on page 0.
    [Fact]
    public async Task Feed_AfterTheMorningRelease_SweepsAtMostEveryTenMinutes()
    {
        await using var context = CreateContext();
        var client = ReleaseFeed();
        var clock = new Clock(Night);
        var service = ServiceAt(client, context, clock);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        clock.Now = Release;
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        client.ClearReceivedCalls();

        clock.Now = Release.AddMinutes(5);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        var soon = client.ReceivedCalls().Count();
        client.ClearReceivedCalls();
        clock.Now = Release.AddMinutes(11);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);

        soon.Should().Be(1);
        client.ReceivedCalls().Should().HaveCount(3);
    }

    // A sweep a failed page cut short is not a sweep: the next poll walks to the cutoff again.
    [Fact]
    public async Task Feed_SweepCutShortByAFailedPage_IsRunAgainOnTheNextPoll()
    {
        await using var context = CreateContext();
        var client = ReleaseFeed();
        var clock = new Clock(Night);
        var service = ServiceAt(client, context, clock);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        var secondPage = EveningForms(0, 300).Skip(73).Take(27).ToList();
        secondPage.Add(
            FeedEntry(
                "0001144879",
                "8-K",
                69,
                new DateTimeOffset(2026, 11, 25, 20, 54, 45, Eastern)
            )
        );
        secondPage.AddRange(EveningForms(100, 72));
        client
            .GetRecentFilings(100, 100, Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromException<List<EdgarRecentFilingEntry>>(new HttpRequestException()),
                _ => Task.FromResult(secondPage)
            );
        clock.Now = Release;

        var cutShort = await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        clock.Now = Release.AddMinutes(1);
        var next = await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);

        cutShort.Should().BeEmpty();
        next.Should().ContainSingle().Which.Should().BeSameAs(AppliedDigital);
    }

    // Away from the release a page holding a read entry ends the walk, so a poll costs one request.
    [Fact]
    public async Task Feed_LaterInTheDay_StopsOnThePageThatOverlapsTheLastPoll()
    {
        await using var context = CreateContext();
        var client = ReleaseFeed();
        var clock = new Clock(Night);
        var service = ServiceAt(client, context, clock);
        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);
        client.ClearReceivedCalls();
        clock.Now = new DateTimeOffset(2026, 11, 27, 19, 0, 0, TimeSpan.Zero);

        await service.DiscoverCompaniesFromRecentFeed([AppliedDigital]);

        await client
            .Received(1)
            .GetRecentFilings(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Feed_PollFailure_IsSwallowed()
    {
        await using var context = CreateContext();
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetRecentFilings(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException());
        client
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);

        var dirty = await CreateService(client, context)
            .DiscoverCompaniesWithNewFilings([Tracked("AAPL", "320193")]);

        dirty.Should().BeEmpty();
    }

    [Fact]
    public async Task DailyIndex_ColdStart_InitializesWatermarkWithoutReplayingHistory()
    {
        await using var context = CreateContext();
        var client = ClientWithFeed();

        await CreateService(client, context)
            .DiscoverCompaniesWithNewFilings([Tracked("AAPL", "320193")]);

        var state = await context.Set<BackfillState>().SingleAsync();
        state.Name.Should().Be(FilingDiscoveryService.DailyIndexStateName);
        state.Floor.Should().Be(LatestFinalDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        await client
            .DidNotReceive()
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DailyIndex_PendingDays_DirtyTrackedFilersAndAdvanceWatermark()
    {
        await using var context = CreateContext();
        EquityIssuer apple = Tracked("AAPL", "320193");
        var pendingDay = LatestFinalDay;
        context
            .Set<BackfillState>()
            .Add(
                new BackfillState
                {
                    Name = FilingDiscoveryService.DailyIndexStateName,
                    Floor = pendingDay.AddDays(-1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            );
        await context.SaveChangesAsync();

        var client = ClientWithFeed();
        client
            .GetDailyIndexForForms(
                pendingDay,
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([
                new EdgarDailyIndexEntry
                {
                    Cik = "320193",
                    FormType = "8-K",
                    AccessionNumber = "0000320193-26-000009",
                    DateFiled = pendingDay,
                },
                // Prefix over-match from the client ("4" also returns 424B2
                // rows) must be re-filtered exactly.
                new EdgarDailyIndexEntry
                {
                    Cik = "320193",
                    FormType = "424B2",
                    AccessionNumber = "0000320193-26-000010",
                    DateFiled = pendingDay,
                },
            ]);

        var dirty = await CreateService(client, context).DiscoverCompaniesWithNewFilings([apple]);

        dirty.Should().ContainSingle().Which.Should().BeSameAs(apple);
        var state = await context.Set<BackfillState>().SingleAsync();
        state.Floor.Should().Be(pendingDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    [Fact]
    public async Task DailyIndex_FetchFailure_HoldsWatermark()
    {
        await using var context = CreateContext();
        var floorDay = LatestFinalDay.AddDays(-1);
        context
            .Set<BackfillState>()
            .Add(
                new BackfillState
                {
                    Name = FilingDiscoveryService.DailyIndexStateName,
                    Floor = floorDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                }
            );
        await context.SaveChangesAsync();

        var client = ClientWithFeed();
        client
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException());

        var dirty = await CreateService(client, context)
            .DiscoverCompaniesWithNewFilings([Tracked("AAPL", "320193")]);

        dirty.Should().BeEmpty();
        var state = await context.Set<BackfillState>().SingleAsync();
        state.Floor.Should().Be(floorDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }
}
