using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.Data.Enums;
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

// Pins #982: SEC stamps every value inside a 10-K with the filing's own
// fy/fp identity, even though comparable-year values inside the same filing
// measure different actual periods. The resolver must label each row by
// the period it measures (derived from PeriodStart / PeriodEnd against
// CommonStock.FiscalYearEndMonth/Day) — not by the filing's identity.
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportPeriodIdentityTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesDbContext> _contexts = [];

    public FinancialFactsImportPeriodIdentityTests(ParadeDbFixture fixture) => _fixture = fixture;

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
    public async Task Import_TenKWithThreeComparableYears_DerivesFiscalYearFromPeriodEndPerCompanyFYE()
    {
        // Apple's FYE is Sept 28 (52/53-week filer, so actual end dates
        // wobble within a few days year over year).
        var apple = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
            FiscalYearEndMonth = 9,
            FiscalYearEndDay = 28,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(apple);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        // One filing (FY2024 10-K) carrying three comparable-year FullYear
        // values for the same concept, all tagged with the filing's fy=2024
        // / fp=FY. Each value's actual period is encoded in start/end.
        CompanyFactValue Annual(DateOnly start, DateOnly end, decimal val) =>
            new()
            {
                Start = start,
                End = end,
                Val = val,
                Accn = "0000320193-24-000123",
                Fy = 2024,
                Fp = "FY",
                Form = "10-K",
                Filed = new DateOnly(2024, 11, 1),
                Frame = null,
            };
        var values = new[]
        {
            Annual(new DateOnly(2023, 09, 30), new DateOnly(2024, 09, 28), 391_035_000_000m),
            Annual(new DateOnly(2022, 09, 25), new DateOnly(2023, 09, 30), 383_285_000_000m),
            Annual(new DateOnly(2021, 09, 26), new DateOnly(2022, 09, 24), 394_328_000_000m),
        };
        var response = new CompanyFactsResponse
        {
            Cik = 320193,
            EntityName = "Apple Inc.",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["RevenueFromContractWithCustomerExcludingAssessedTax"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new() { ["USD"] = values.ToList() },
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
            .OrderByDescending(f => f.PeriodEnd)
            .ToListAsync(CancellationToken.None);

        facts.Should().HaveCount(3);
        // Each row's (FiscalYear, FiscalPeriod) must reflect the period it
        // actually measures — not the filing's fy/fp.
        facts[0].FiscalYear.Should().Be(2024);
        facts[0].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        facts[1].FiscalYear.Should().Be(2023);
        facts[1].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
        facts[2].FiscalYear.Should().Be(2022);
        facts[2].FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
    }

    [Fact]
    public async Task Import_CompanyWithoutFye_FallsBackToFilingSuppliedFiscalYear()
    {
        // A company without ingested fiscal-year-end metadata gets the
        // pre-fix behavior — value.Fy ?? value.End.Year — preserved.
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "ZZZZ",
            Name = "Unknown FYE Corp.",
            Cik = "0000999999",
            FiscalYearEndMonth = null,
            FiscalYearEndDay = null,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        var response = new CompanyFactsResponse
        {
            Cik = 999999,
            EntityName = "Unknown FYE Corp.",
            Facts = new()
            {
                ["us-gaap"] = new()
                {
                    ["Revenues"] = new CompanyFactConcept
                    {
                        Label = "Revenues",
                        Units = new()
                        {
                            ["USD"] =
                            [
                                new CompanyFactValue
                                {
                                    Start = new DateOnly(2023, 1, 1),
                                    End = new DateOnly(2023, 12, 31),
                                    Val = 100m,
                                    Accn = "0000999999-24-000001",
                                    Fy = 2023,
                                    Fp = "FY",
                                    Form = "10-K",
                                    Filed = new DateOnly(2024, 1, 15),
                                },
                            ],
                        },
                    },
                },
            },
        };

        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("0000999999").Returns(response);

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
        var fact = await verify
            .Set<FinancialFact>()
            .SingleAsync(f => f.CommonStockId == stock.Id, CancellationToken.None);

        fact.FiscalYear.Should().Be(2023);
        fact.FiscalPeriod.Should().Be(SecFiscalPeriod.FullYear);
    }
}
