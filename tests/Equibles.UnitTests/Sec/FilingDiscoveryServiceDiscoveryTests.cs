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

    // Ownership forms, which EDGAR disseminates at once even after hours, one a minute back from the newest.
    private static List<EdgarRecentFilingEntry> OwnershipForms(
        int first,
        int count,
        DateTimeOffset newest
    ) =>
        [
            .. Enumerable
                .Range(first, count)
                .Select(i => FeedEntry("0000999999", "4", i, newest.AddMinutes(first - i))),
        ];

    // Recorded at the 06:00 ET release of 2026-11-27 (EDGAR closed for Thanksgiving): Applied Digital's 8-K
    // accepted 11-25 at 20:54 ET sat on the feed's second page and Vishay Precision's (17:49 ET) further down,
    // among evening entries the night's polls had read. The walk stopped on the first page, and both waited
    // over four hours for a worker restart's full walk.
    [Fact]
    public async Task Feed_AfterTheMorningRelease_WalksPastReadEntriesToTheHeldFilings()
    {
        await using var context = CreateContext();
        EquityIssuer appliedDigital = Tracked("APLD", "1144879");
        EquityIssuer vishayPrecision = Tracked("VPG", "1487952");
        var evening = new DateTimeOffset(2026, 11, 25, 21, 56, 0, Eastern);
        var nightPage = OwnershipForms(0, 100, evening);
        var morning = new DateTimeOffset(2026, 11, 27, 6, 5, 0, Eastern);
        List<EdgarRecentFilingEntry> morningFirst =
        [
            .. Enumerable
                .Range(0, 5)
                .Select(i => FeedEntry("0000888888", "424B2", i, morning.AddMinutes(-i))),
            .. nightPage.Take(95),
        ];
        List<EdgarRecentFilingEntry> morningSecond =
        [
            .. nightPage.Skip(95),
            FeedEntry(
                "0001144879",
                "8-K",
                69,
                new DateTimeOffset(2026, 11, 25, 20, 54, 45, Eastern)
            ),
            .. OwnershipForms(100, 94, evening.AddMinutes(-100)),
        ];
        List<EdgarRecentFilingEntry> morningThird =
        [
            FeedEntry(
                "0001487952",
                "8-K",
                38325,
                new DateTimeOffset(2026, 11, 25, 17, 49, 20, Eastern)
            ),
            .. OwnershipForms(194, 99, new DateTimeOffset(2026, 11, 25, 17, 45, 0, Eastern)),
        ];
        var client = ClientWithFeed();
        client
            .GetRecentFilings(0, 100, Arg.Any<CancellationToken>())
            .Returns(nightPage, morningFirst);
        client.GetRecentFilings(100, 100, Arg.Any<CancellationToken>()).Returns([], morningSecond);
        client.GetRecentFilings(200, 100, Arg.Any<CancellationToken>()).Returns(morningThird);
        var clock = new Clock(new DateTimeOffset(2026, 11, 27, 10, 50, 0, TimeSpan.Zero));
        var service = new FilingDiscoveryService(
            client,
            new BackfillStateRepository(context),
            Options.Create(new DocumentScraperOptions()),
            Substitute.For<ILogger<FilingDiscoveryService>>(),
            clock: clock
        );

        var night = await service.DiscoverCompaniesFromRecentFeed([
            appliedDigital,
            vishayPrecision,
        ]);
        clock.Now = new DateTimeOffset(2026, 11, 27, 11, 6, 0, TimeSpan.Zero);
        var released = await service.DiscoverCompaniesFromRecentFeed([
            appliedDigital,
            vishayPrecision,
        ]);

        night.Should().BeEmpty();
        released.Should().HaveCount(2).And.Contain(appliedDigital).And.Contain(vishayPrecision);
    }

    // Away from the release a page holding a read entry ends the walk, so a poll costs one request.
    [Fact]
    public async Task Feed_LaterInTheDay_StopsOnThePageThatOverlapsTheLastPoll()
    {
        await using var context = CreateContext();
        var afternoon = new DateTimeOffset(2026, 11, 27, 14, 0, 0, Eastern);
        var firstPage = OwnershipForms(0, 100, afternoon);
        var client = ClientWithFeed();
        client.GetRecentFilings(0, 100, Arg.Any<CancellationToken>()).Returns(firstPage);
        client.GetRecentFilings(100, 100, Arg.Any<CancellationToken>()).Returns([]);
        var clock = new Clock(afternoon.AddMinutes(1));
        var service = new FilingDiscoveryService(
            client,
            new BackfillStateRepository(context),
            Options.Create(new DocumentScraperOptions()),
            Substitute.For<ILogger<FilingDiscoveryService>>(),
            clock: clock
        );
        await service.DiscoverCompaniesFromRecentFeed([Tracked("AAPL", "320193")]);
        client.ClearReceivedCalls();
        clock.Now = afternoon.AddMinutes(2);

        await service.DiscoverCompaniesFromRecentFeed([Tracked("AAPL", "320193")]);

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
