using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.GovernmentContracts.Data;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Integrations.GovernmentContracts.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract: the persisted scan checkpoint advances per fully-completed window (independently
// of whether that window inserted rows) and is monotonic, so a transport abort mid-scan can
// no longer rewind the cursor to the start of the range and replay it every cycle — the
// freeze that kept the government-contracts backfill stuck at 2022-01-13 and re-flooded the
// Errors page.
public class GovernmentContractsImportServiceCheckpointTests
{
    // Must match GovernmentContractsImportService.ScanStateName — pins the persisted row key.
    private const string ScanStateName = "award-scan";

    [Fact]
    public async Task Import_TransportAbortMidScan_CheckpointHoldsAtLastCompletedWindow_ResumesThereNextCycle()
    {
        // A five-day scan in one-day windows where the third window's fetch throws
        // HttpRequestException. The first two windows complete (advancing the checkpoint), the
        // third aborts the cycle. The checkpoint must hold at the second window's end, and the
        // NEXT cycle must resume at the third window — not restart the whole range.
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);

        var scopeFactory = ScopeFactory(options);
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = today.AddDays(-4).ToDateTime(TimeOnly.MinValue) }
        );
        // Lookback of 1 keeps the trailing-rescan pull-back out of this backfill assertion, so
        // the resume point is exactly the aborted window.
        var scraperOptions = Options.Create(
            new GovernmentContractsScraperOptions
            {
                WindowDays = 1,
                RescanLookbackDays = 1,
                MinimumAwardAmount = 1_000_000m,
            }
        );

        var failingClient = Substitute.For<IUsaSpendingClient>();
        ReturnsEmptyForAllWindows(failingClient);
        failingClient
            .GetContractAwards(
                today.AddDays(-2),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("USAspending bad spell"));

        var act = () =>
            NewService(scopeFactory, failingClient, scraperOptions, workerOptions)
                .Import(CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
        ReadCheckpoint(options)
            .Should()
            .Be(
                today.AddDays(-3),
                "the checkpoint holds at the last completed window when the cycle aborts"
            );

        // Next cycle, the spell has cleared: resume at the aborted window, don't restart.
        var healthyClient = Substitute.For<IUsaSpendingClient>();
        ReturnsEmptyForAllWindows(healthyClient);

        await NewService(scopeFactory, healthyClient, scraperOptions, workerOptions)
            .Import(CancellationToken.None);

        await healthyClient
            .DidNotReceive()
            .GetContractAwards(
                today.AddDays(-4),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        await healthyClient
            .DidNotReceive()
            .GetContractAwards(
                today.AddDays(-3),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        await healthyClient
            .Received()
            .GetContractAwards(
                today.AddDays(-2),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        ReadCheckpoint(options)
            .Should()
            .Be(today, "the resumed cycle scans through to today and advances the checkpoint");
    }

    [Fact]
    public async Task Import_EmptyWindows_StillAdvanceTheCheckpoint()
    {
        // Every window returns zero awards (nothing inserted, so max(ActionDate) never moves).
        // The checkpoint must still advance to the final window — otherwise a long run of
        // empty-for-us windows would re-scan from the same start forever.
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);

        var scopeFactory = ScopeFactory(options);
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = today.AddDays(-2).ToDateTime(TimeOnly.MinValue) }
        );
        var scraperOptions = Options.Create(
            new GovernmentContractsScraperOptions
            {
                WindowDays = 1,
                RescanLookbackDays = 1,
                MinimumAwardAmount = 1_000_000m,
            }
        );

        var client = Substitute.For<IUsaSpendingClient>();
        ReturnsEmptyForAllWindows(client);

        await NewService(scopeFactory, client, scraperOptions, workerOptions)
            .Import(CancellationToken.None);

        ReadCheckpoint(options).Should().Be(today);
    }

    [Fact]
    public async Task Import_TrailingRescan_DoesNotRewindTheCheckpoint()
    {
        // A caught-up checkpoint (at today) with a 7-day lookback re-scans today-6..today each
        // cycle. Re-completing those earlier windows must not rewind the checkpoint — the
        // monotonic guard keeps it at today.
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SeedCompany(options);
        using (var seed = NewContext(options))
        {
            seed.Add(
                new GovernmentContractsScanState
                {
                    Name = ScanStateName,
                    LastCompletedWindowEnd = today,
                    UpdatedAt = DateTime.UtcNow,
                }
            );
            seed.SaveChanges();
        }

        var scopeFactory = ScopeFactory(options);
        var workerOptions = Options.Create(new WorkerOptions());
        var scraperOptions = Options.Create(
            new GovernmentContractsScraperOptions
            {
                WindowDays = 1,
                RescanLookbackDays = 7,
                MinimumAwardAmount = 1_000_000m,
            }
        );

        var client = Substitute.For<IUsaSpendingClient>();
        ReturnsEmptyForAllWindows(client);

        await NewService(scopeFactory, client, scraperOptions, workerOptions)
            .Import(CancellationToken.None);

        // Proves the trailing rescan actually ran (earlier window fetched) yet did not rewind.
        await client
            .Received()
            .GetContractAwards(
                today.AddDays(-(7 - 1)),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
        ReadCheckpoint(options).Should().Be(today);
    }

    private static void ReturnsEmptyForAllWindows(IUsaSpendingClient client) =>
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));

    private static GovernmentContractsImportService NewService(
        IServiceScopeFactory scopeFactory,
        IUsaSpendingClient client,
        IOptions<GovernmentContractsScraperOptions> scraperOptions,
        IOptions<WorkerOptions> workerOptions
    ) =>
        new(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            scraperOptions,
            workerOptions,
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

    private static void SeedCompany(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var seed = NewContext(options);
        // One named company so BuildLookup is non-empty and the empty-universe guard passes.
        seed.Add(
            new CommonStock
            {
                Ticker = "LMT",
                Name = "Lockheed Martin Corporation",
                Cik = "1",
            }
        );
        seed.SaveChanges();
    }

    private static DateOnly? ReadCheckpoint(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        using var ctx = NewContext(options);
        return ctx.Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .FirstOrDefault(s => s.Name == ScanStateName)
            ?.LastCompletedWindowEnd;
    }

    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new GovernmentContractsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<CommonStockRepository>();
        services.AddScoped<GovernmentContractRepository>();
        services.AddScoped<GovernmentContractsScanStateRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
