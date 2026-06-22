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

public class GovernmentContractsImportServiceWindowContinueTests
{
    [Fact]
    public async Task Import_WindowThrowsNonTransportException_ContinuesScanningRemainingWindows()
    {
        // Contract (from Import's window-loop comment): only a transport failure
        // (HttpRequestException) is systemic enough to abort the cycle. A window-specific
        // failure — anything that is NOT an HttpRequestException — is reported but falls
        // through so the scan continues to the remaining windows. With a two-day scan split
        // into one-day windows where the first window throws a non-transport exception, the
        // client must therefore still be invoked for the later window: the cycle is NOT
        // aborted (which would leave it called exactly once).
        var options = NewDbOptions();
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
            .GetContractAwards(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<decimal>())
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
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
