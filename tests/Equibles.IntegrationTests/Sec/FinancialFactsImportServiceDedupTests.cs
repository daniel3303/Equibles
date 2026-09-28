using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly FinancialFactBatchInterceptor _batches = new();

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
        var ctx = _fixture.CreateDbContext(options => options.AddInterceptors(_batches));
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
                sp.GetService(typeof(ISharesOutstandingProvider))
                    .Returns(Substitute.For<ISharesOutstandingProvider>());
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
        EquityIssuer apple = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(apple);
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
            .Where(f => f.EquityIssuerId == apple.Id)
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

    [Theory]
    [InlineData(null, new[] { 3 })]
    [InlineData(1, new[] { 1, 1, 1 })]
    [InlineData(2, new[] { 2, 1 })]
    [InlineData(0, new[] { 3 })]
    [InlineData(-5, new[] { 3 })]
    public async Task Import_ConfiguredBatchSize_PersistsEveryFactBeforeAdvancingCheckpoint(
        int? batchSize,
        int[] expectedBatches
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(issuer);
            await seed.SaveChangesAsync();
        }
        var values = Enumerable
            .Range(2021, 3)
            .Select(year => new CompanyFactValue
            {
                Start = new DateOnly(year, 1, 1),
                End = new DateOnly(year, 12, 31),
                Val = year,
                Accn = $"0000320193-{year % 100:00}-000001",
                Fy = year,
                Fp = "FY",
                Form = "10-K",
                Filed = new DateOnly(year + 1, 2, 1),
            })
            .ToList();
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFacts(issuer.Cik)
            .Returns(
                new CompanyFactsResponse
                {
                    Cik = 320193,
                    Facts = new()
                    {
                        ["us-gaap"] = new()
                        {
                            ["Revenues"] = new CompanyFactConcept
                            {
                                Label = "Revenues",
                                Units = new() { ["USD"] = values },
                            },
                        },
                    },
                }
            );
        var scopeFactory = CreateScopeFactory();
        var reporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        var sut = new FinancialFactsImportService(
            scopeFactory,
            client,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            reporter,
            persistenceOptions: batchSize.HasValue
                ? Options.Create(
                    new FinancialFactsPersistenceOptions { InsertBatchSize = batchSize.Value }
                )
                : null
        );

        await sut.Import(issuer, CancellationToken.None);

        _batches.FactBatchSizes.Should().Equal(expectedBatches);
        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<FinancialFact>().OrderBy(f => f.Value).Select(f => f.Value).ToListAsync())
            .Should()
            .Equal(2021m, 2022m, 2023m);
        var status = await verify.Set<FinancialFactsSyncStatus>().SingleAsync();
        status.LastFiledDateSeen.Should().Be(new DateOnly(2024, 2, 1));
    }
}
