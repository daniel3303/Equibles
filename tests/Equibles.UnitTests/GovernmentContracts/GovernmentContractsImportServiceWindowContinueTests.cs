using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
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

public class GovernmentContractsImportServiceWindowContinueTests
{
    [Fact]
    public async Task Import_FirstWindowThrowsHttp4xx_ReportsFailureAndContinuesScanningLaterWindows()
    {
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using (var seed = NewContext(options))
        {
            seed.Add(
                new CommonStock
                {
                    Ticker = "LMT",
                    Name = "Lockheed Martin Corporation",
                    Cik = "1",
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory(options);
        var firstWindow = today.AddDays(-1);
        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));
        client
            .GetContractAwards(
                firstWindow,
                firstWindow,
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException(
                    "USAspending rejected the window",
                    inner: null,
                    statusCode: HttpStatusCode.UnprocessableEntity
                )
            );

        var service = new GovernmentContractsImportService(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                }
            ),
            Options.Create(
                new WorkerOptions { MinSyncDate = firstWindow.ToDateTime(TimeOnly.MinValue) }
            ),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

        await service.Import(CancellationToken.None);

        await client
            .Received(1)
            .GetContractAwards(today, today, Arg.Any<decimal>(), Arg.Any<CancellationToken>());

        using var context = NewContext(options);
        var reported = await context.Set<Error>().AsNoTracking().SingleAsync();
        reported.Source.Should().Be(ErrorSource.GovernmentContractsScraper);
        reported.Context.Should().Be("GovernmentContractsImport.ImportWindow");
        reported.Message.Should().Contain("USAspending rejected the window");
        reported.RequestSummary.Should().Be($"window: {firstWindow}..{firstWindow}");
    }

    [Fact]
    public async Task Import_HistoricalWindowFails_FreezesTheCheckpointBehindTheFailedWindow()
    {
        // The data-hole guard. A 4xx on a historical window is stepped over so the rest of the
        // scan still runs — but the checkpoint records the CONTIGUOUS frontier, so it must stop
        // at the day before the failure even though later windows completed. Advancing it to the
        // last successful window would drop the failed day's awards for good the moment it aged
        // out of the trailing rescan lookback.
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var scanStart = today.AddDays(-5);
        var failingWindow = today.AddDays(-3);

        using (var seed = NewContext(options))
        {
            seed.Add(
                new CommonStock
                {
                    Ticker = "LMT",
                    Name = "Lockheed Martin Corporation",
                    Cik = "1",
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory(options);
        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));
        client
            .GetContractAwards(
                failingWindow,
                failingWindow,
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException(
                    "USAspending rejected the window",
                    inner: null,
                    statusCode: HttpStatusCode.UnprocessableEntity
                )
            );

        var service = new GovernmentContractsImportService(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                }
            ),
            Options.Create(
                new WorkerOptions { MinSyncDate = scanStart.ToDateTime(TimeOnly.MinValue) }
            ),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

        await service.Import(CancellationToken.None);

        // Every later window still ran — the failure was stepped over, not fatal.
        await client
            .Received(1)
            .GetContractAwards(today, today, Arg.Any<decimal>(), Arg.Any<CancellationToken>());

        using var context = NewContext(options);
        var checkpoint = await context
            .Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .SingleAsync();

        checkpoint
            .LastCompletedWindowEnd.Should()
            .Be(
                failingWindow.AddDays(-1),
                "the frontier must freeze at the last contiguously-scanned day, not jump to the "
                    + "last successful window and strand the failed one"
            );
    }

    [Fact]
    public async Task Import_TrailingRescanWindowFails_RetractsTheCheckpointBehindIt()
    {
        // The steady-state hole, and the one freezing alone cannot close. Once the scan has
        // caught up, the checkpoint LEADS the trailing lookback window, so a failure inside
        // that window sits BEHIND the frontier: declining to advance changes nothing, and the
        // next cycle's lookback slides one day forward and steps over the failed day for good.
        // The checkpoint must therefore be pulled BACK behind the failure.
        var options = NewDbOptions();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lookbackStart = today.AddDays(-6);

        using (var seed = NewContext(options))
        {
            seed.Add(
                new CommonStock
                {
                    Ticker = "LMT",
                    Name = "Lockheed Martin Corporation",
                    Cik = "1",
                }
            );
            // A caught-up scan: the frontier already reaches yesterday, well ahead of the
            // trailing window about to be re-covered.
            seed.Add(
                new GovernmentContractsScanState
                {
                    Name = "award-scan",
                    LastCompletedWindowEnd = today.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow,
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory(options);
        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));
        client
            .GetContractAwards(
                lookbackStart,
                lookbackStart,
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException(
                    "USAspending rejected the window",
                    inner: null,
                    statusCode: HttpStatusCode.UnprocessableEntity
                )
            );

        var service = new GovernmentContractsImportService(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            Options.Create(
                new GovernmentContractsScraperOptions
                {
                    WindowDays = 1,
                    MinimumAwardAmount = 1_000_000m,
                    RescanLookbackDays = 7,
                }
            ),
            Options.Create(new WorkerOptions()),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

        await service.Import(CancellationToken.None);

        using var context = NewContext(options);
        var checkpoint = await context
            .Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .SingleAsync();

        checkpoint
            .LastCompletedWindowEnd.Should()
            .Be(
                lookbackStart.AddDays(-1),
                "an unresolved day behind the frontier must pull the frontier back to it, or "
                    + "the sliding lookback abandons it next cycle"
            );
    }

    [Fact]
    public void Import_HistoricalWindowFails_NextCycleResumesAtTheFailedWindow()
    {
        // The other half of the guard: freezing the checkpoint is only useful if the next cycle
        // actually re-covers the hole. Feed the frozen checkpoint back through the cursor policy
        // together with a watermark from the LATER windows that succeeded — the resume date must
        // land on the failed window, not past it.
        var today = new DateOnly(2026, 7, 17);
        var failedWindow = new DateOnly(2026, 3, 2);

        var start = GovernmentContractsImportService.ResolveStartDate(
            latestActionDate: new DateOnly(2026, 3, 20),
            checkpointEnd: failedWindow.AddDays(-1),
            today,
            rescanLookbackDays: 7,
            new WorkerOptions()
        );

        start.Should().Be(failedWindow);
    }

    [Fact]
    public async Task Import_WindowThrowsNonTransportException_ContinuesScanningRemainingWindows()
    {
        // Contract (from Import's window-loop comment): a non-HTTP failure is window-specific.
        // It is reported and falls through so the scan continues to the remaining windows, so
        // with a two-day scan split into one-day windows the client must still be invoked for
        // the later window — an abort would leave it called exactly once. It must ALSO freeze
        // the frontier: the failure is a hole like any other, and a guard that only fired for
        // HttpRequestException would pass the call-count assertion while silently banking a
        // checkpoint past the unscanned day.
        var options = NewDbOptions();
        var failingWindow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        using (var seed = NewContext(options))
        {
            seed.Add(
                new CommonStock
                {
                    Ticker = "LMT",
                    Name = "Lockheed Martin Corporation",
                    Cik = "1",
                }
            );
            await seed.SaveChangesAsync();
        }

        var scopeFactory = ScopeFactory(options);

        var client = Substitute.For<IUsaSpendingClient>();
        client
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new List<UsaSpendingAwardRecord>()));
        client
            .GetContractAwards(
                failingWindow,
                failingWindow,
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("window-specific parse failure"));

        // Empty GovernmentContract table -> DetermineStartDate falls back to MinSyncDate.
        // One day back with one-day windows yields at least two windows.
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = DateTime.UtcNow.Date.AddDays(-1) }
        );
        var scraperOptions = Options.Create(
            new GovernmentContractsScraperOptions
            {
                WindowDays = 1,
                MinimumAwardAmount = 1_000_000m,
            }
        );

        var service = new GovernmentContractsImportService(
            scopeFactory,
            NullLogger<GovernmentContractsImportService>.Instance,
            client,
            new RecipientResolver(scopeFactory),
            scraperOptions,
            workerOptions,
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

        await service.Import(CancellationToken.None);

        // > 1 invocation proves the non-transport failure did not abort the cycle after the
        // first window. An abort would leave exactly one call.
        client
            .ReceivedCalls()
            .Should()
            .HaveCountGreaterThan(
                1,
                "a window-specific (non-transport) failure must not abort the scan"
            );

        // ...and the later window that succeeded must not have banked a frontier past the hole.
        using var context = NewContext(options);
        var checkpoint = await context
            .Set<GovernmentContractsScanState>()
            .AsNoTracking()
            .SingleOrDefaultAsync();

        checkpoint
            .Should()
            .BeNull(
                "the first window failed, so nothing behind it was ever contiguously scanned "
                    + "and no frontier may be recorded"
            );
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
                new ErrorsModuleConfiguration(),
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
        services.AddScoped<ErrorRepository>();
        services.AddScoped<ErrorManager>();
        services.AddScoped<GovernmentContractRepository>();
        services.AddScoped<GovernmentContractsScanStateRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
