using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Sibling to FinancialFactsScraperWorkerDoWorkTests (which covers the happy-path
/// CIK-bearing iteration). The cold-start branch — every CommonStock row lacks a
/// CIK (CompanySync hasn't run yet, or returns are still being assigned) — is
/// structurally distinct: the worker must signal `RequestRetrySoon()` so the
/// next cycle fires after `NotReadyRetryInterval` (~2 minutes) instead of the
/// configured `SleepIntervalHours` (default 6+). A refactor that dropped that
/// call would compile and the existing happy-path test would stay green, while
/// every fresh deploy silently waited hours before the first scrape attempt.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsScraperWorkerNoCikStocksRetrySoonTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FinancialFactsScraperWorkerNoCikStocksRetrySoonTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(ctx));
                sp.GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private sealed class TestableFinancialFactsScraperWorker : FinancialFactsScraperWorker
    {
        public TestableFinancialFactsScraperWorker(
            ILogger<FinancialFactsScraperWorker> logger,
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinancialFactsScraperOptions> options,
            IConfiguration configuration
        )
            : base(logger, scopeFactory, errorReporter, options, configuration) { }

        public Task InvokeDoWork(CancellationToken ct) => DoWork(ct);
    }

    [Fact]
    public async Task DoWork_NoStocksWithCik_RequestsRetrySoonAndSkipsImport()
    {
        // Seed a stock WITHOUT a CIK. The .Where(s => s.Cik != null && s.Cik != "")
        // filter must exclude it, leaving stockIds.Count == 0.
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>()
                .Add(
                    new CommonStock
                    {
                        Id = Guid.NewGuid(),
                        Ticker = "NOCIK",
                        Name = "Pre-sync placeholder",
                    }
                );
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var worker = new TestableFinancialFactsScraperWorker(
            Substitute.For<ILogger<FinancialFactsScraperWorker>>(),
            CreateScopeFactory(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FinancialFactsScraperOptions { SleepIntervalHours = 6 }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { ["Sec:ContactEmail"] = "ops@example.com" }
                )
                .Build()
        );

        await worker.InvokeDoWork(CancellationToken.None);

        // No imports should have happened — no FinancialFactsSyncStatus rows written.
        await using var verify = _fixture.CreateDbContext();
        var statusCount = await verify
            .Set<FinancialFactsSyncStatus>()
            .CountAsync(CancellationToken.None);
        statusCount.Should().Be(0);

        // RequestRetrySoon sets the private `_retrySoonRequested` field on
        // BaseScraperWorker; assert that flag is true so the loop wakes after
        // NotReadyRetryInterval, not SleepIntervalHours.
        var retryField = typeof(BaseScraperWorker).GetField(
            "_retrySoonRequested",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var retrySoon = (bool)retryField.GetValue(worker)!;
        retrySoon
            .Should()
            .BeTrue(
                "the worker must signal a short retry instead of sleeping the full SleepInterval when no CIK-bearing stocks exist"
            );
    }
}
