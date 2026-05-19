using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// FinancialFactsScraperWorker.DoWork was uncovered: it queries CommonStocks
/// with a non-empty CIK, then per-stock opens a scope and runs the import
/// service. Pins the discover-and-iterate contract: only Cik-bearing stocks
/// reach the import (and therefore the sync-status checkpoint table), and a
/// Cik-less stock is silently filtered.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsScraperWorkerDoWorkTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FinancialFactsScraperWorkerDoWorkTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private IServiceScopeFactory CreateScopeFactory(Func<FinancialFactsImportService> importFactory)
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
                sp.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(ctx));
                sp.GetService(typeof(FinancialFactsImportService)).Returns(_ => importFactory());
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
    public async Task DoWork_IteratesOnlyCikBearingStocks_AndCheckpointsEachViaImport()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var msft = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp",
            Cik = "0000789019",
        };
        var noCik = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NOCIK",
            Name = "No CIK Co",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(apple, msft, noCik);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFacts(Arg.Any<string>())
            .Returns(
                new CompanyFactsResponse
                {
                    Cik = 0,
                    EntityName = "X",
                    Facts = new(),
                }
            );

        IServiceScopeFactory scopeFactory = null!;
        scopeFactory = CreateScopeFactory(() =>
            new FinancialFactsImportService(
                scopeFactory,
                secEdgarClient,
                Substitute.For<ILogger<FinancialFactsImportService>>(),
                Substitute.For<ErrorReporter>(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<ErrorReporter>>()
                )
            )
        );

        var worker = new TestableFinancialFactsScraperWorker(
            Substitute.For<ILogger<FinancialFactsScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new FinancialFactsScraperOptions { SleepIntervalHours = 1 }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string> { ["Sec:ContactEmail"] = "ops@example.com" }
                )
                .Build()
        );

        await worker.InvokeDoWork(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var statusStockIds = await verify
            .Set<FinancialFactsSyncStatus>()
            .Select(s => s.CommonStockId)
            .ToListAsync(CancellationToken.None);
        statusStockIds
            .Should()
            .BeEquivalentTo(
                [apple.Id, msft.Id],
                "DoWork must iterate each Cik-bearing stock through Import (which checkpoints sync status), and skip Cik-less stocks via the GetAll().Where(Cik != null) filter"
            );
    }
}
