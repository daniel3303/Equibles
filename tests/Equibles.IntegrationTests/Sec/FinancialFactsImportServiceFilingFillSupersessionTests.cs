using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
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

/// <summary>
/// A 10-Q/10-K consolidated fill from the filing's own XBRL stands in only until Company Facts
/// carries the accession. The import that does carry it must take over the rows it re-emits and
/// delete the fills it does not state, while extension, dimensional and foreign-form rows and fills
/// on accessions the API still lacks survive.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportServiceFilingFillSupersessionTests : IAsyncLifetime
{
    private const string Cik = "0001517175";
    private const string EmittedAccession = "0001517175-26-000028";
    private const string ForeignAccession = "0001517175-26-000030";
    private const string MissingAccession = "0001517175-26-000040";

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FinancialFactsImportServiceFilingFillSupersessionTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Import_EmittedAccession_TakesOverMatchingFillsAndDeletesTheRest()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CHEF",
            Name: "Chefs' Warehouse",
            Cik: Cik
        );
        var revenues = new FinancialConcept { Taxonomy = FactTaxonomy.UsGaap, Tag = "Revenues" };
        var eps = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
        };
        var extension = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Custom,
            Tag = "chef:CustomerCount",
        };
        FinancialFact matched,
            unstated,
            extensionFill,
            dimensional,
            foreignFill,
            missingFill;
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(issuer, revenues, eps, extension);
            matched = Fill(issuer, revenues, EmittedAccession, DocumentType.TenQ, 1_168_613_000m);
            unstated = Fill(issuer, eps, EmittedAccession, DocumentType.TenQ, 0.76m, "USD/shares");
            extensionFill = Fill(issuer, extension, EmittedAccession, DocumentType.TenQ, 42m);
            dimensional = Fill(issuer, revenues, EmittedAccession, DocumentType.TenQ, 500m);
            dimensional.DimensionsKey = new string('a', 64);
            foreignFill = Fill(issuer, eps, ForeignAccession, DocumentType.SixK, 1m, "USD/shares");
            missingFill = Fill(issuer, eps, MissingAccession, DocumentType.TenQ, 2m, "USD/shares");
            seed.AddRange(matched, unstated, extensionFill, dimensional, foreignFill, missingFill);
            await seed.SaveChangesAsync();
        }
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFacts(Cik)
            .Returns(
                Response(
                    ("Revenues", "USD", EmittedAccession, 1_168_613_000m),
                    ("EarningsPerShareDiluted", "USD/shares", ForeignAccession, 1m)
                )
            );

        await Sut(client).Import(issuer, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var rows = await verify
            .Set<FinancialFact>()
            .AsNoTracking()
            .Where(f => f.EquityIssuerId == issuer.Id)
            .ToDictionaryAsync(f => f.Id);
        rows.Should().ContainKey(matched.Id, "the API re-emitted it, so it keeps its row");
        rows[matched.Id].Source.Should().Be(FinancialFactSource.CompanyFacts);
        rows[matched.Id].Frame.Should().Be("CY2026Q2");
        rows.Should().NotContainKey(unstated.Id, "the API carries the accession but not this fact");
        rows.Should().ContainKey(extensionFill.Id);
        rows.Should().ContainKey(dimensional.Id);
        rows[foreignFill.Id].Source.Should().Be(FinancialFactSource.CompanyFacts);
        rows[missingFill.Id].Source.Should().Be(FinancialFactSource.FilingXbrl);
        rows.Should().HaveCount(5);
    }

    [Fact]
    public async Task Import_NothingNewFiled_StillSupersedesFillsOnALateProcessedAccession()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CHEF",
            Name: "Chefs' Warehouse",
            Cik: Cik
        );
        var eps = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(issuer, eps);
            await seed.SaveChangesAsync();
        }
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFacts(Cik)
            .Returns(Response(("Revenues", "USD", EmittedAccession, 1_168_613_000m)));
        await Sut(client).Import(issuer, CancellationToken.None);

        // The watermark already covers the accession's filed date when the fill shows up.
        FinancialFact unstated;
        await using (var seed = _fixture.CreateDbContext())
        {
            unstated = Fill(issuer, eps, EmittedAccession, DocumentType.TenQ, 0.76m, "USD/shares");
            unstated.FinancialConceptId = eps.Id;
            unstated.FinancialConcept = null;
            seed.Add(unstated);
            await seed.SaveChangesAsync();
        }
        await Sut(client).Import(issuer, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<FinancialFact>().AnyAsync(f => f.Id == unstated.Id))
            .Should()
            .BeFalse("the nothing-new skip must not keep a superseded fill");
    }

    private FinancialFactsImportService Sut(ISecEdgarClient client) =>
        new(
            ScopeFactory(),
            client,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

    private static FinancialFact Fill(
        EquityIssuer issuer,
        FinancialConcept concept,
        string accession,
        DocumentType form,
        decimal value,
        string unit = "USD"
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            FinancialConcept = concept,
            Unit = unit,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2026, 3, 28),
            PeriodEnd = new DateOnly(2026, 6, 26),
            Value = value,
            FiscalYear = 2026,
            FiscalPeriod = SecFiscalPeriod.Q2,
            Form = form,
            FiledDate = new DateOnly(2026, 7, 29),
            AccessionNumber = accession,
            Source = FinancialFactSource.FilingXbrl,
        };

    private static CompanyFactsResponse Response(
        params (string Tag, string Unit, string Accession, decimal Value)[] facts
    ) =>
        new()
        {
            Cik = 1517175,
            EntityName = "Chefs' Warehouse",
            Facts = new()
            {
                ["us-gaap"] = facts
                    .GroupBy(f => f.Tag)
                    .ToDictionary(
                        g => g.Key,
                        g => new CompanyFactConcept
                        {
                            Label = g.Key,
                            Units = g.GroupBy(f => f.Unit)
                                .ToDictionary(
                                    u => u.Key,
                                    u =>
                                        u.Select(f => new CompanyFactValue
                                            {
                                                Start = new DateOnly(2026, 3, 28),
                                                End = new DateOnly(2026, 6, 26),
                                                Val = f.Value,
                                                Accn = f.Accession,
                                                Fy = 2026,
                                                Fp = "Q2",
                                                Form =
                                                    f.Accession == ForeignAccession
                                                        ? "6-K"
                                                        : "10-Q",
                                                Filed = new DateOnly(2026, 7, 29),
                                                Frame = "CY2026Q2",
                                            })
                                            .ToList()
                                ),
                        }
                    ),
            },
        };

    private IServiceScopeFactory ScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = _fixture.CreateDbContext();
                _contexts.Add(context);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                provider
                    .GetService(typeof(ISharesOutstandingProvider))
                    .Returns(Substitute.For<ISharesOutstandingProvider>());
                provider
                    .GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(context));
                provider
                    .GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(context));
                provider
                    .GetService(typeof(DocumentRepository))
                    .Returns(new DocumentRepository(context));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return scopeFactory;
    }
}
