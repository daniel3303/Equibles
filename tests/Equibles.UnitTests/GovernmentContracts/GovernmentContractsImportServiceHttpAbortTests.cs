using System.Net;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
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
    public async Task Import_ResponseLessHttpRequestException_AbortsCycleWithoutScanningLaterWindows()
    {
        await AssertSystemicFailureAborts(new HttpRequestException("USAspending unreachable"));
    }

    [Fact]
    public async Task Import_Http5xxResponse_AbortsCycleWithoutScanningLaterWindows()
    {
        await AssertSystemicFailureAborts(
            new HttpRequestException(
                "USAspending unavailable",
                inner: null,
                statusCode: HttpStatusCode.ServiceUnavailable
            )
        );
    }

    [Fact]
    public async Task Import_Timeout_AbortsCycleWithoutScanningLaterWindows()
    {
        await AssertSystemicFailureAborts(new TaskCanceledException("USAspending timed out"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Import_SystemicSub500Status_AbortsCycleWithoutScanningLaterWindows(
        HttpStatusCode status
    )
    {
        // These reach the import loop only after UsaSpendingClient exhausted its own retry
        // ladder (429/408) or hit a rejection that is about our credentials/endpoint rather
        // than the dates asked for (401/403/404). None of them is window-specific, so
        // continuing would restart the full backoff ladder against every remaining window —
        // hammering a source that already said stop. They must abort like a 5xx.
        await AssertSystemicFailureAborts(
            new HttpRequestException($"USAspending returned {status}", inner: null, status)
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Import_RequestLevel4xx_ScansEveryRemainingWindowWithoutThrowing(
        HttpStatusCode status
    )
    {
        // The counterpart to the theory above, pinning the boundary from the other side: a
        // rejection of THIS window (422 out-of-range dates, 400 malformed) says nothing about
        // the rest of the scan, so every remaining window must still be attempted and nothing
        // may escape to the worker.
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
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException($"USAspending returned {status}", inner: null, status)
            );

        // Five days back with one-day windows yields six windows.
        var service = NewService(scopeFactory, client, DateTime.UtcNow.Date.AddDays(-5));

        var act = () => service.Import(CancellationToken.None);

        await act.Should().NotThrowAsync();
        await client
            .Received(6)
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static async Task AssertSystemicFailureAborts(Exception failure)
    {
        // A response-less HTTP failure, exhausted 5xx response, or timeout is systemic:
        // later windows would fail identically, so the first failure must propagate after
        // one client call and let the worker own outage reporting and backoff.
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
            .ThrowsAsync(failure);

        // Empty GovernmentContract table -> DetermineStartDate falls back to MinSyncDate.
        // Five days back with one-day windows yields six windows; an un-aborted scan would
        // call the client six times.
        var service = NewService(scopeFactory, client, DateTime.UtcNow.Date.AddDays(-5));

        var act = () => service.Import(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.GetType().Should().Be(failure.GetType());
        await client
            .Received(1)
            .GetContractAwards(
                Arg.Any<DateOnly>(),
                Arg.Any<DateOnly>(),
                Arg.Any<decimal>(),
                Arg.Any<CancellationToken>()
            );
    }

    private static GovernmentContractsImportService NewService(
        IServiceScopeFactory scopeFactory,
        IUsaSpendingClient client,
        DateTime minSyncDate
    ) =>
        new(
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
            Options.Create(new WorkerOptions { MinSyncDate = minSyncDate }),
            new ErrorReporter(scopeFactory, NullLogger<ErrorReporter>.Instance)
        );

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
