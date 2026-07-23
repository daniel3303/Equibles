using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.GovernmentContracts.Data;
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

public class GovernmentContractsImportServiceHttpAbortTests
{
    [Fact]
    public async Task Import_FirstWindowThrowsHttpRequestException_AbortsCycleWithoutScanningLaterWindows()
    {
        // Contract (from Import's window-loop comment): a transport-level failure —
        // HttpRequestException, the API unreachable even after the client's own retries —
        // is systemic, so every remaining window would fail identically. The cycle must
        // STOP and RETHROW rather than hammer the API once per window: the worker's
        // consecutive-failure streak owns reporting (one Error row per outage), so the
        // service must not write its own row per cycle — that's exactly the flood that
        // put 13 identical rows on the Errors page in one day. With a multi-day scan
        // split into one-day windows, the first window failing with HttpRequestException
        // must propagate and leave every later window un-fetched: the client is called
        // exactly once, not once per window.
        var options = NewDbOptions();
        using (var seed = NewContext(options))
        {
            // One named company so BuildLookup is non-empty and the empty-universe guard passes.
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
            .ThrowsAsync(new HttpRequestException("USAspending unreachable"));

        // Empty GovernmentContract table -> DetermineStartDate falls back to MinSyncDate.
        // Five days back with one-day windows yields six windows; an un-aborted scan would
        // call the client six times.
        var workerOptions = Options.Create(
            new WorkerOptions { MinSyncDate = DateTime.UtcNow.Date.AddDays(-5) }
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

        var act = () => service.Import(CancellationToken.None);

        // The transport failure must propagate (the worker's streak reporting depends on
        // seeing the throw) after exactly one client call — no later window is scanned.
        await act.Should().ThrowAsync<HttpRequestException>();
        await client
            .Received(1)
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
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
