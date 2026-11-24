using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// A foreign filer's quarter read from its 6-K's statement table: the statement and fact tools list
/// it beside the 20-F year, and every other reader of the store never sees it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialToolsReportedStatementTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly QuarterStart = new(2026, 1, 1);
    private static readonly DateOnly QuarterEnd = new(2026, 3, 31);

    [Fact]
    public async Task GetFinancialStatement_ListsTheQuarterReadFromAStatementTable()
    {
        await Seed();
        var sut = new FinancialStatementTools(
            new StatementFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

        var income = await sut.GetFinancialStatement("FRGN", statement: "income");
        var balance = await sut.GetFinancialStatement(
            "FRGN",
            statement: "balance",
            year: 2026,
            period: "Q1"
        );

        income.Should().Contain("— FY2026 Q1:").And.Contain("120,000,000").And.Contain("6-K");
        balance.Should().Contain("900,000,000").And.Contain("2026-03-31");
    }

    [Fact]
    public async Task GetFinancialFact_ListsTheQuarterBesideTheAnnualFigure()
    {
        await Seed();
        var sut = new FinancialFactsTools(
            new StatementFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

        var result = await sut.GetFinancialFact("FRGN", "revenue");

        result.Should().Contain("120,000,000").And.Contain("400,000,000").And.Contain("6-K");
    }

    [Fact]
    public async Task EveryOtherReader_SeesOnlyTheStructuredFigures()
    {
        var issuer = await Seed();
        var facts = new FinancialFactRepository(DbContext);

        (await facts.GetAll().ToListAsync())
            .Should()
            .OnlyContain(f => f.Form == DocumentType.TwentyF);
        (await facts.GetConsolidatedByIssuerId(issuer.Id).CountAsync()).Should().Be(1);
        (await facts.GetByIssuerId(issuer.Id).CountAsync()).Should().Be(1);
        (
            await new StatementFactRepository(DbContext)
                .GetConsolidatedByIssuerId(issuer.Id)
                .CountAsync()
        )
            .Should()
            .Be(3);
    }

    private async Task<EquityIssuer> Seed()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "FRGN",
            Name: "Foreign Filer",
            Cik: "0001999998"
        );
        var revenue = Concept("Revenues");
        var assets = Concept("Assets");
        DbContext.Set<EquityIssuer>().Add(issuer);
        DbContext.Set<FinancialConcept>().AddRange(revenue, assets);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                new FinancialFact
                {
                    EquityIssuerId = issuer.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2025, 1, 1),
                    PeriodEnd = new DateOnly(2025, 12, 31),
                    Value = 400_000_000m,
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TwentyF,
                    FiledDate = new DateOnly(2026, 3, 18),
                    AccessionNumber = "0001999998-26-000001",
                },
                Reported(issuer, revenue, QuarterStart, 120_000_000m),
                Reported(issuer, assets, QuarterEnd, 900_000_000m)
            );
        await DbContext.SaveChangesAsync();
        return issuer;
    }

    private static FinancialConcept Concept(string tag) =>
        new()
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = tag,
            Label = tag,
        };

    private static FinancialFact Reported(
        EquityIssuer issuer,
        FinancialConcept concept,
        DateOnly start,
        decimal value
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            FinancialConceptId = concept.Id,
            Unit = "USD",
            PeriodType = start == QuarterEnd ? FactPeriodType.Instant : FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = QuarterEnd,
            Value = value,
            FiscalYear = 2026,
            FiscalPeriod = SecFiscalPeriod.Q1,
            Form = DocumentType.SixK,
            FiledDate = new DateOnly(2026, 5, 12),
            AccessionNumber = "0001999998-26-000007",
            Source = FinancialFactSource.ReportedStatement,
        };
}
