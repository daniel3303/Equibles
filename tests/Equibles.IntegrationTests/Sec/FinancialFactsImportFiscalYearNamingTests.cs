using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
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

// How a company names its fiscal year is read on every facts sync from the fiscal year SEC
// records for each annual report, and stored beside the year end. Stored fiscal years keep the
// calendar year the period ends in; only the label a reader sees follows the company.
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportFiscalYearNamingTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportFiscalYearNamingTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var sharesProvider = Substitute.For<ISharesOutstandingProvider>();
        sharesProvider
            .GetCurrentSharesOutstanding(Arg.Any<EquityIssuer>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<long?>(null));
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(ctx));
                sp.GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(ctx));
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(ctx));
                sp.GetService(typeof(ISharesOutstandingProvider)).Returns(sharesProvider);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private async Task<EquityIssuer> SeedRetailer(int? storedNaming = null)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "HD",
            Name: "The Home Depot, Inc.",
            Cik: "0000354950",
            FiscalYearEndMonth: 1,
            FiscalYearEndDay: 31
        );
        stock.FiscalYearLabelOffset = storedNaming;
        await using var seed = _fixture.CreateDbContext();
        seed.Set<EquityIssuer>().Add(stock);
        await seed.SaveChangesAsync(CancellationToken.None);
        return stock;
    }

    // One annual report as Company Facts serves it: its own year and two comparatives, every
    // value under the filing's fiscal year.
    private static IEnumerable<CompanyFactValue> AnnualReport(
        string accession,
        int secFiscalYear,
        DateOnly filed,
        params (DateOnly Start, DateOnly End)[] years
    ) =>
        years.Select(
            (year, index) =>
                new CompanyFactValue
                {
                    Start = year.Start,
                    End = year.End,
                    Val = 150_000_000_000m + index,
                    Accn = accession,
                    Fy = secFiscalYear,
                    Fp = "FY",
                    Form = "10-K",
                    Filed = filed,
                }
        );

    private static readonly (DateOnly Start, DateOnly End) Year2025 = (
        new DateOnly(2025, 2, 3),
        new DateOnly(2026, 2, 1)
    );
    private static readonly (DateOnly Start, DateOnly End) Year2024 = (
        new DateOnly(2024, 1, 29),
        new DateOnly(2025, 2, 2)
    );
    private static readonly (DateOnly Start, DateOnly End) Year2023 = (
        new DateOnly(2023, 1, 30),
        new DateOnly(2024, 1, 28)
    );

    private FinancialFactsImportService Importer(params CompanyFactValue[] values)
    {
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient
            .GetCompanyFacts("0000354950")
            .Returns(
                new CompanyFactsResponse
                {
                    Cik = 354950,
                    EntityName = "The Home Depot, Inc.",
                    Facts = new()
                    {
                        ["us-gaap"] = new()
                        {
                            ["Revenues"] = new CompanyFactConcept
                            {
                                Label = "Revenues",
                                Units = new() { ["USD"] = values.ToList() },
                            },
                        },
                    },
                }
            );
        return new FinancialFactsImportService(
            CreateScopeFactory(),
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
    }

    private async Task<int?> StoredNaming(Guid issuerId)
    {
        await using var verify = _fixture.CreateDbContext();
        return await verify
            .Set<EquityIssuer>()
            .Where(issuer => issuer.Id == issuerId)
            .Select(issuer => issuer.FiscalYearLabelOffset)
            .SingleAsync(CancellationToken.None);
    }

    private static CompanyFactValue[] TwoReportsNamedForTheStartYear() =>
        [
            .. AnnualReport(
                "0001628280-26-019436",
                2025,
                new DateOnly(2026, 3, 18),
                Year2025,
                Year2024,
                Year2023
            ),
            .. AnnualReport(
                "0000354950-25-000085",
                2024,
                new DateOnly(2025, 3, 21),
                Year2024,
                Year2023
            ),
        ];

    [Fact]
    public async Task Import_ReportsNamedForTheYearTheyStartIn_StoresTheNamingAndKeepsStoredYears()
    {
        var stock = await SeedRetailer();

        await Importer(TwoReportsNamedForTheStartYear()).Import(stock, CancellationToken.None);

        (await StoredNaming(stock.Id)).Should().Be(-1);
        stock.FiscalYearLabelOffset.Should().Be(-1);
        await using var verify = _fixture.CreateDbContext();
        var years = await verify
            .Set<FinancialFact>()
            .Where(fact => fact.EquityIssuerId == stock.Id)
            .Select(fact => new { fact.PeriodEnd, fact.FiscalYear })
            .Distinct()
            .OrderByDescending(fact => fact.PeriodEnd)
            .ToListAsync(CancellationToken.None);
        // The comparatives carry the filing's year on the wire and are no evidence: each stored
        // year is still the calendar year its period ends in.
        years.Select(year => year.FiscalYear).Should().Equal(2026, 2025, 2024);
    }

    [Fact]
    public async Task Import_NothingNewFiled_StillStoresTheNaming()
    {
        var stock = await SeedRetailer();
        await Importer(TwoReportsNamedForTheStartYear()).Import(stock, CancellationToken.None);
        await using (var reset = _fixture.CreateDbContext())
        {
            await reset
                .Set<EquityIssuer>()
                .Where(issuer => issuer.Id == stock.Id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(issuer => issuer.FiscalYearLabelOffset, (int?)null)
                );
        }
        stock.FiscalYearLabelOffset = null;

        await Importer(TwoReportsNamedForTheStartYear()).Import(stock, CancellationToken.None);

        (await StoredNaming(stock.Id)).Should().Be(-1);
    }

    [Fact]
    public async Task Import_PayloadWithoutAnAnnualReport_LeavesTheStoredNaming()
    {
        var stock = await SeedRetailer(storedNaming: -1);
        var quarter = new CompanyFactValue
        {
            Start = new DateOnly(2026, 2, 2),
            End = new DateOnly(2026, 5, 3),
            Val = 39_000_000_000m,
            Accn = "0000354950-26-000140",
            Fy = 2026,
            Fp = "Q1",
            Form = "10-Q",
            Filed = new DateOnly(2026, 5, 20),
        };

        await Importer(quarter).Import(stock, CancellationToken.None);

        (await StoredNaming(stock.Id)).Should().Be(-1);
    }

    [Fact]
    public async Task Import_ReportsThatDisagree_ClearTheStoredNaming()
    {
        var stock = await SeedRetailer(storedNaming: -1);
        CompanyFactValue[] values =
        [
            // The newest report carries the calendar year it ends in, the one before it the
            // year it starts in: one of them is mistyped, so neither settles the naming.
            .. AnnualReport("0001628280-26-019436", 2026, new DateOnly(2026, 3, 18), Year2025),
            .. AnnualReport("0000354950-25-000085", 2024, new DateOnly(2025, 3, 21), Year2024),
        ];

        await Importer(values).Import(stock, CancellationToken.None);

        (await StoredNaming(stock.Id)).Should().BeNull();
    }
}
