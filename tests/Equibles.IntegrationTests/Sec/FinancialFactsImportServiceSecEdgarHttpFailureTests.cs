using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the network-failure containment in <c>FinancialFactsImportService.Import</c>:
/// when SEC EDGAR's Company Facts endpoint throws <c>HttpRequestException</c>
/// (timeout, transient 5xx, DNS hiccup), the per-stock cycle must log a
/// warning and return cleanly — never rethrow up to the worker loop. A
/// regression removing the try/catch would let a single SEC outage kill the
/// entire FinancialFacts scraping worker on the next stock. Discriminative
/// signals: no FinancialFact / SyncStatus / FinancialConcept rows are
/// written, and no ErrorReporter entry is filed (warnings only).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceSecEdgarHttpFailureTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FinancialFactsImportServiceSecEdgarHttpFailureTests(ParadeDbFixture fixture) =>
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
                sp.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    [Fact]
    public async Task Import_GetCompanyFactsThrowsHttpRequestException_ReturnsWithoutCrashOrWrites()
    {
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFacts("0000320193")
            .Returns<Task<Equibles.Integrations.Sec.Models.Responses.CompanyFactsResponse>>(_ =>
                throw new HttpRequestException("simulated transient SEC EDGAR failure")
            );

        var errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            errorReporter
        );

        // Must not throw — the cycle must keep moving to the next stock.
        var act = () => sut.Import(apple, CancellationToken.None);
        await act.Should().NotThrowAsync();

        await using var verify = _fixture.CreateDbContext();
        var conceptCount = await verify.Set<FinancialConcept>().CountAsync(CancellationToken.None);
        conceptCount.Should().Be(0, "no concept upsert may run when the HTTP call failed");
        var factCount = await verify
            .Set<FinancialFact>()
            .CountAsync(f => f.CommonStockId == apple.Id, CancellationToken.None);
        factCount.Should().Be(0, "no fact rows may be written when the HTTP call failed");
        var sync = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleOrDefaultAsync(s => s.CommonStockId == apple.Id, CancellationToken.None);
        sync.Should().BeNull("no sync-status checkpoint may be written when the HTTP call failed");
        await errorReporter
            .DidNotReceive()
            .Report(
                Arg.Any<ErrorSource>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }
}
