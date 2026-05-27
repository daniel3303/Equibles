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
/// Adversarial test of the Critical dedup contract. SEC Company Facts emits the
/// same (concept, unit, period, accession) tuple more than once — frame vs
/// non-frame duplicates and restatement re-emits. The import service's natural
/// key is a Postgres unique index, and `ON CONFLICT DO UPDATE` rejects a batch
/// that targets the same row twice ("cannot affect row a second time"). The
/// contract is: duplicates collapse to exactly one row, keeping the
/// latest-`filed` value — and the import never crashes. Fed two values sharing
/// the full key with different `filed`/`val`, a missing dedup would persist
/// zero rows (the batch throws, the per-company catch swallows it); the fix
/// must persist exactly one row carrying the newer value.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceDedupTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportServiceDedupTests(ParadeDbFixture fixture) => _fixture = fixture;

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
    public async Task Import_DuplicateConceptPeriodAccessionTuples_CollapsesToLatestFiledOneRow()
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
            await seed.SaveChangesAsync();
        }

        // Same taxonomy/tag/unit/period/accession, two filings: the older one
        // reports 100, the restatement (later `filed`) reports 110. This is the
        // exact shape that makes Postgres ON CONFLICT see the same row twice.
        var older = new CompanyFactValue
        {
            Start = new DateOnly(2023, 1, 1),
            End = new DateOnly(2023, 12, 31),
            Val = 100m,
            Accn = "0000320193-24-000001",
            Fy = 2023,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2024, 1, 15),
            Frame = null,
        };
        var newer = new CompanyFactValue
        {
            Start = new DateOnly(2023, 1, 1),
            End = new DateOnly(2023, 12, 31),
            Val = 110m,
            Accn = "0000320193-24-000001",
            Fy = 2023,
            Fp = "FY",
            Form = "10-K",
            Filed = new DateOnly(2024, 6, 1),
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
                        Units = new() { ["USD"] = [older, newer] },
                    },
                },
            },
        };

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("0000320193").Returns(response);

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
        var facts = await verify
            .Set<FinancialFact>()
            .Where(f => f.CommonStockId == apple.Id)
            .ToListAsync(CancellationToken.None);

        facts.Should().HaveCount(1, "duplicate tuples must collapse to one row");
        facts[0].Value.Should().Be(110m, "the latest-filed value wins");
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
