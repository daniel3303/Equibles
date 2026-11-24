using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Net income reads one sign on the fact, compare and statement tools. PicPay's 2025 20-F tags
/// its BRL 1,091,487,000 profit as a loss: total profit 1,141,606,000 equals it plus the
/// 50,119,000 minority share only once negated. The same filing's 2024 comparative is tagged
/// right, and an older year no filing checks is left out, total profit included.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialToolsOwnersProfitSignTests : ParadeDbMcpTestBase
{
    public FinancialToolsOwnersProfitSignTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools FactsSut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private FinancialStatementTools StatementSut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    private FinancialConcept Concept(string tag)
    {
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.IfrsFull,
            Tag = tag,
            Label = tag,
        };
        DbContext.Set<FinancialConcept>().Add(concept);
        return concept;
    }

    private void AddYear(
        EquityIssuer stock,
        FinancialConcept concept,
        int year,
        decimal value,
        string accession,
        DateOnly filed
    ) =>
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "BRL",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(year, 1, 1),
                    PeriodEnd = new DateOnly(year, 12, 31),
                    Value = value,
                    FiscalYear = year,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TwentyF,
                    FiledDate = filed,
                    AccessionNumber = accession,
                }
            );

    private async Task Seed()
    {
        var owners = Concept("ProfitLossAttributableToOwnersOfParent");
        var total = Concept("ProfitLoss");
        var minority = Concept("ProfitLossAttributableToNoncontrollingInterests");
        var revenue = Concept("Revenue");

        var picpay = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "PICS",
            Name: "PicPay Holdings",
            Cik: "0002048566"
        );
        DbContext.Set<EquityIssuer>().Add(picpay);
        const string annual2025 = "0001213900-26-049950";
        DateOnly filed2025 = new(2026, 4, 30);
        AddYear(picpay, owners, 2025, -1_091_487_000m, annual2025, filed2025);
        AddYear(picpay, total, 2025, 1_141_606_000m, annual2025, filed2025);
        AddYear(picpay, minority, 2025, 50_119_000m, annual2025, filed2025);
        AddYear(picpay, revenue, 2025, 9_000_000_000m, annual2025, filed2025);
        AddYear(picpay, owners, 2024, 217_574_000m, annual2025, filed2025);
        AddYear(picpay, total, 2024, 251_788_000m, annual2025, filed2025);
        AddYear(picpay, minority, 2024, 34_214_000m, annual2025, filed2025);
        // No minority share is tagged for 2023, so nothing checks its sign.
        const string annual2023 = "0001213900-24-030001";
        DateOnly filed2023 = new(2024, 4, 30);
        AddYear(picpay, owners, 2023, -34_512_000m, annual2023, filed2023);
        AddYear(picpay, total, 2023, 34_512_000m, annual2023, filed2023);
        AddYear(picpay, revenue, 2023, 5_000_000_000m, annual2023, filed2023);
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialFact_OwnersProfitTaggedWithTheOppositeSign_ReadsTheProvenSign()
    {
        await Seed();

        var result = await FactsSut().GetFinancialFact("PICS", "net-income");

        result.Should().Contain("| 2025-01-01 | 2025-12-31 | 2025 | FY | 1,091,487,000 | BRL |");
        result.Should().Contain("| 2024-01-01 | 2024-12-31 | 2024 | FY | 217,574,000 | BRL |");
        result.Should().NotContain("-1,091,487,000");
        result.Should().NotContain("34,512,000");
        result.Should().Contain(OwnersProfitSign.NegatedNote);
    }

    [Fact]
    public async Task GetFinancialFact_AsOriginallyReported_KeepsTheTaggedSign()
    {
        await Seed();

        var result = await FactsSut()
            .GetFinancialFact("PICS", "net-income", asOriginallyReported: true);

        result.Should().Contain("-1,091,487,000");
        result.Should().NotContain(OwnersProfitSign.NegatedNote);
    }

    [Fact]
    public async Task CompareFinancialFact_OwnersProfitTaggedWithTheOppositeSign_ComparesTheProvenSign()
    {
        await Seed();
        var sut = FactsSut();

        var proven = await sut.CompareFinancialFact(
            ["PICS"],
            "net-income",
            fiscalYear: 2025,
            fiscalPeriod: "FY"
        );
        var unchecked2023 = await sut.CompareFinancialFact(
            ["PICS"],
            "net-income",
            fiscalYear: 2023,
            fiscalPeriod: "FY"
        );

        proven.Should().Contain("1,091,487,000 |");
        proven.Should().NotContain("-1,091,487,000");
        unchecked2023.Should().NotContain("34,512,000");
    }

    [Fact]
    public async Task GetFinancialStatement_OwnersProfitTaggedWithTheOppositeSign_NetIncomeReadsTheProvenSign()
    {
        await Seed();
        var sut = StatementSut();

        var proven = await sut.GetFinancialStatement(
            "PICS",
            statement: "income",
            year: 2025,
            period: "FY"
        );
        var unchecked2023 = await sut.GetFinancialStatement(
            "PICS",
            statement: "income",
            year: 2023,
            period: "FY"
        );

        proven.Should().Contain("| Net Income | 1,091,487,000 | BRL | Reported |");
        proven.Should().Contain(OwnersProfitSign.NegatedNote);
        unchecked2023.Should().Contain("5,000,000,000");
        unchecked2023.Should().NotContain("34,512,000");
    }
}
