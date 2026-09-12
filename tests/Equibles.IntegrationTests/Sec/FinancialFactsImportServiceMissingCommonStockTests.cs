using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
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

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the contract from GH-1591: when a CommonStock row is removed during the
/// network round-trip to SEC EDGAR (the long-running step inside
/// <see cref="FinancialFactsImportService.Import"/>), no FinancialFact or
/// FinancialFactsSyncStatus row must be written for the stale id. Without the
/// guard, Postgres rejects every UpsertSyncStatus and FlushFacts call with
/// FK_*_CommonStock_CommonStockId and the per-cycle activity feed fills with
/// orphan-id errors.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceMissingCommonStockTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportServiceMissingCommonStockTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
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
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
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
    public async Task Import_CommonStockDeletedDuringNetworkCall_DoesNotWriteOrphanRows()
    {
        EquityIssuer apple = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(apple);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var value = new CompanyFactValue
        {
            Start = new DateOnly(2023, 1, 1),
            End = new DateOnly(2023, 12, 31),
            Val = 100m,
            Accn = "0000320193-24-000001",
            Fy = 2023,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2024, 1, 15),
            Frame = "CY2023",
        };
        var response = new CompanyFactsResponse
        {
            Cik = 320193,
            EntityName = "Apple Inc.",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new() { ["USD"] = [value] },
                    },
                },
            },
        };

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFacts("0000320193")
            .Returns(_ =>
            {
                // Race: CompanySync removes the stock during the SEC EDGAR network
                // call. Hooking GetCompanyFacts is the natural injection point —
                // every write that follows in Import references the now-stale id.
                using var deleteCtx = _fixture.CreateDbContext();
                EquityIssuer staleApple = deleteCtx
                    .Set<EquityIssuer>()
                    .Include(issuer => issuer.Presentation)
                    .Include(issuer => issuer.Securities)
                        .ThenInclude(security => security.Listings)
                    .Single(s => s.Id == apple.Id);
                deleteCtx.Remove(staleApple.Presentation);
                deleteCtx.RemoveRange(
                    staleApple.Securities.SelectMany(security => security.Listings)
                );
                deleteCtx.RemoveRange(staleApple.Securities);
                deleteCtx.Set<EquityIssuer>().Remove(staleApple);
                deleteCtx.SaveChanges();
                return Task.FromResult(response);
            });

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

        await sut.Import(apple, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();

        // Diagnostic: confirm the callback actually ran and deleted apple.
        var stillExists = await verify
            .Set<EquityIssuer>()
            .AnyAsync(s => s.Id == apple.Id, CancellationToken.None);
        stillExists.Should().BeFalse("the GetCompanyFacts callback should have deleted apple");

        // Discriminative assertion: the post-fix guard returns early — before
        // ResolveConcepts upserts any FinancialConcept. Pre-fix, ResolveConcepts
        // runs and inserts the row, so a count > 0 means the guard didn't fire.
        var conceptCount = await verify.Set<FinancialConcept>().CountAsync(CancellationToken.None);
        conceptCount
            .Should()
            .Be(0, "the missing-parent guard must short-circuit before ResolveConcepts runs");

        var facts = await verify
            .Set<FinancialFact>()
            .Where(f => f.EquityIssuerId == apple.Id)
            .ToListAsync(CancellationToken.None);
        facts.Should().BeEmpty("no FinancialFact row should be written for a deleted parent");

        var syncStatus = await verify
            .Set<FinancialFactsSyncStatus>()
            .SingleOrDefaultAsync(s => s.EquityIssuerId == apple.Id, CancellationToken.None);
        syncStatus
            .Should()
            .BeNull("no FinancialFactsSyncStatus row should be written for a deleted parent");

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
