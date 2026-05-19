using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Contract (class XML doc): the importer is idempotent and checkpoints sync
/// state. When SEC returns a company with no facts at all, Import must still
/// advance the sync status (with a null last-filed checkpoint) and return
/// cleanly — not error and not skip the checkpoint. This pins the
/// empty-payload guard plus the private UpsertSyncStatus upsert path, both
/// previously uncovered.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceEmptyFactsTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FinancialFactsImportServiceEmptyFactsTests(ParadeDbFixture fixture) =>
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
    public async Task Import_ResponseWithNoFacts_CheckpointsNullSyncStatusWithoutError()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "ZZZZ",
            Name = "No Facts Co.",
            Cik = "0000000123",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFacts("0000000123")
            .Returns(
                new CompanyFactsResponse
                {
                    Cik = 123,
                    EntityName = "No Facts Co.",
                    Facts = new(),
                }
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

        await sut.Import(stock, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var status = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleOrDefaultAsync(s => s.CommonStockId == stock.Id, CancellationToken.None);
        status.Should().NotBeNull("an empty-facts company must still be checkpointed");
        status!
            .LastFiledDateSeen.Should()
            .BeNull("there were no filings to record a high-water mark");
        await errorReporter
            .DidNotReceive()
            .Report(
                Arg.Any<Equibles.Errors.Data.Models.ErrorSource>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }
}
