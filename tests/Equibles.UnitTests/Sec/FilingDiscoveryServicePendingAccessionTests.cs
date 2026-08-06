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
/// Pins the pending-accession retry contract: a filing the realtime feed
/// flagged stays pending until the company's submissions enumeration confirms
/// its accession under that CIK (the submissions JSON lags acceptance, so the
/// enumeration run right after the flag can miss it — the EBS after-the-bell
/// 8-K case, where the company was enumerated 91 seconds after acceptance,
/// found nothing, and the filing silently waited ~14h for the daily index).
/// While pending, the company is re-dirtied at most once per retry interval
/// and at most the configured retry count, confirmation removes the entry per
/// CIK, expiry abandons it to the daily-index backstop, a filer that leaves
/// the tracked universe is dropped, and a failed feed poll does not stop the
/// retries.
/// </summary>
[Collection("SecFilingDiscoveryStatics")]
public class FilingDiscoveryServicePendingAccessionTests
{
    private const string Accession = "0001367644-26-000080";

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
        DocumentScraperOptions options
    ) =>
        new(
            secEdgarClient,
            new BackfillStateRepository(context),
            Options.Create(options),
            Substitute.For<ILogger<FilingDiscoveryService>>()
        );

    // Every poll may run (no feed throttle) so consecutive Discover calls model
    // consecutive scrape cycles; the retry gate is what each test varies.
    private static DocumentScraperOptions OptionsWithRetryGate(int retrySeconds) =>
        new() { RecentFeedPollSeconds = 0, FeedPendingRetrySeconds = retrySeconds };

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

    private static EdgarRecentFilingEntry EbsEightK() =>
        new()
        {
            Cik = "0001367644",
            FormType = "8-K",
            AccessionNumber = Accession,
        };

    public FilingDiscoveryServicePendingAccessionTests()
    {
        FilingDiscoveryService.ResetCrossCycleStateForTests();
    }

    [Fact]
    public async Task UnconfirmedFeedAccession_RedirtiesCompanyOnLaterPass()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var service = CreateService(ClientWithFeed(EbsEightK()), context, OptionsWithRetryGate(0));

        var firstPass = await service.DiscoverCompaniesWithNewFilings([company]);
        // Second pass: the feed entry is already seen, so only the pending
        // ledger can dirty the company again.
        var secondPass = await service.DiscoverCompaniesWithNewFilings([company]);

        firstPass.Should().ContainSingle().Which.Should().BeSameAs(company);
        secondPass.Should().ContainSingle().Which.Should().BeSameAs(company);
    }

    [Fact]
    public async Task ConfirmedFeedAccession_IsNotReflagged()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var service = CreateService(ClientWithFeed(EbsEightK()), context, OptionsWithRetryGate(0));

        await service.DiscoverCompaniesWithNewFilings([company]);
        // The bare CIK the enumeration reports matches the feed's padded one
        // numerically.
        service.MarkAccessionsEnumerated([(Accession, "1367644")]);
        var secondPass = await service.DiscoverCompaniesWithNewFilings([company]);

        secondPass.Should().BeEmpty("the enumeration confirmed the accession");
    }

    [Fact]
    public async Task ConfirmationUnderAnotherCik_DoesNotReleaseThisCompany()
    {
        await using var context = CreateContext();
        var issuer = Tracked("EBS", "1367644");
        var owner = Tracked("HOLD", "2000001");
        // The same accession appears once per associated entity, each with its
        // own CIK — confirming one side must not release the other.
        var ownerSideEntry = new EdgarRecentFilingEntry
        {
            Cik = "0002000001",
            FormType = "8-K",
            AccessionNumber = Accession,
        };
        var service = CreateService(
            ClientWithFeed(EbsEightK(), ownerSideEntry),
            context,
            OptionsWithRetryGate(0)
        );

        await service.DiscoverCompaniesWithNewFilings([issuer, owner]);
        service.MarkAccessionsEnumerated([(Accession, "1367644")]);
        var secondPass = await service.DiscoverCompaniesWithNewFilings([issuer, owner]);

        secondPass
            .Should()
            .ContainSingle("only the issuer's side was confirmed")
            .Which.Should()
            .BeSameAs(owner);
    }

    [Fact]
    public async Task UnconfirmedFeedAccession_InsideRetryGate_IsNotReflagged()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        // Real-sized gate: an immediate next pass must not burn an extra
        // enumeration on a filing flagged seconds ago.
        var service = CreateService(
            ClientWithFeed(EbsEightK()),
            context,
            OptionsWithRetryGate(retrySeconds: 300)
        );

        await service.DiscoverCompaniesWithNewFilings([company]);
        var secondPass = await service.DiscoverCompaniesWithNewFilings([company]);

        secondPass.Should().BeEmpty("the retry interval has not elapsed");
    }

    [Fact]
    public async Task UnconfirmedFeedAccession_RetriesExhausted_IsAbandoned()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var options = OptionsWithRetryGate(0);
        options.FeedPendingMaxRetries = 1;
        var service = CreateService(ClientWithFeed(EbsEightK()), context, options);

        var flagPass = await service.DiscoverCompaniesWithNewFilings([company]);
        var retryPass = await service.DiscoverCompaniesWithNewFilings([company]);
        var abandonedPass = await service.DiscoverCompaniesWithNewFilings([company]);

        flagPass.Should().ContainSingle("the feed flag dirties the company");
        retryPass.Should().ContainSingle("the single allowed retry runs");
        abandonedPass.Should().BeEmpty("the retry budget is spent");
    }

    [Fact]
    public async Task ExpiredFeedAccession_IsAbandoned()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var options = OptionsWithRetryGate(0);
        // Negative expiry makes every pending entry instantly stale without
        // needing a clock seam.
        options.FeedPendingExpiryMinutes = -1;
        var service = CreateService(ClientWithFeed(EbsEightK()), context, options);

        var firstPass = await service.DiscoverCompaniesWithNewFilings([company]);
        var secondPass = await service.DiscoverCompaniesWithNewFilings([company]);

        firstPass.Should().ContainSingle("the feed flag itself still dirties the company");
        secondPass.Should().BeEmpty("the expired entry is dropped for the daily index to backstop");
    }

    [Fact]
    public async Task PendingAccessionForUntrackedCompany_IsDropped()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var service = CreateService(ClientWithFeed(EbsEightK()), context, OptionsWithRetryGate(0));

        await service.DiscoverCompaniesWithNewFilings([company]);
        // The filer leaves the tracked universe: its pending entry is removed,
        // so re-tracking it later must not resurrect the retry.
        var withoutCompany = await service.DiscoverCompaniesWithNewFilings([]);
        var trackedAgain = await service.DiscoverCompaniesWithNewFilings([company]);

        withoutCompany.Should().BeEmpty();
        trackedAgain.Should().BeEmpty("the pending entry was dropped with the company");
    }

    [Fact]
    public async Task FailedFeedPoll_DoesNotStopPendingRetries()
    {
        await using var context = CreateContext();
        var company = Tracked("EBS", "1367644");
        var client = ClientWithFeed(EbsEightK());
        var service = CreateService(client, context, OptionsWithRetryGate(0));

        await service.DiscoverCompaniesWithNewFilings([company]);
        // The feed layer is best-effort; the pending ledger must keep retrying
        // even when the next poll dies.
        client
            .GetRecentFilings(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("feed down"));
        var secondPass = await service.DiscoverCompaniesWithNewFilings([company]);

        secondPass.Should().ContainSingle().Which.Should().BeSameAs(company);
    }
}
