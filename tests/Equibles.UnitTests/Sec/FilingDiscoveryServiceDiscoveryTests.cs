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
        DocumentScraperOptions options = null
    ) =>
        new(
            secEdgarClient,
            new BackfillStateRepository(context),
            Options.Create(options ?? new DocumentScraperOptions()),
            Substitute.For<ILogger<FilingDiscoveryService>>()
        );

    private static CommonStock Tracked(string ticker, string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Cik = cik,
        };

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
        var apple = Tracked("AAPL", "320193");
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

    [Fact]
    public async Task Feed_UntrackedCikOrUnsyncedForm_IsIgnored()
    {
        await using var context = CreateContext();
        var apple = Tracked("AAPL", "320193");
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
        var apple = Tracked("AAPL", "320193");
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
        var apple = Tracked("AAPL", "320193");
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
