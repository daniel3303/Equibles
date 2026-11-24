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
        DateOnly filed,
        DocumentType form = null
    ) =>
        AddSpan(
            stock,
            concept,
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            SecFiscalPeriod.FullYear,
            value,
            accession,
            filed,
            form ?? DocumentType.TwentyF
        );

    private void AddSpan(
        EquityIssuer stock,
        FinancialConcept concept,
        DateOnly start,
        DateOnly end,
        SecFiscalPeriod period,
        decimal value,
        string accession,
        DateOnly filed,
        DocumentType form
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
                    PeriodStart = start,
                    PeriodEnd = end,
                    Value = value,
                    FiscalYear = end.Year,
                    FiscalPeriod = period,
                    Form = form,
                    FiledDate = filed,
                    AccessionNumber = accession,
                }
            );

    private FinancialConcept _owners;
    private FinancialConcept _total;
    private FinancialConcept _minority;
    private FinancialConcept _revenue;
    private EquityIssuer _picpay;

    private async Task Seed()
    {
        var owners = _owners = Concept("ProfitLossAttributableToOwnersOfParent");
        var total = _total = Concept("ProfitLoss");
        var minority = _minority = Concept("ProfitLossAttributableToNoncontrollingInterests");
        var revenue = _revenue = Concept("Revenue");

        var picpay = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "PICS",
            Name: "PicPay Holdings",
            Cik: "0002048566"
        );
        DbContext.Set<EquityIssuer>().Add(picpay);
        _picpay = picpay;
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

    [Fact]
    public async Task GetFinancialStatement_YearFiledOnA10K_IsReadAsTheHistoryIsRead()
    {
        // A year the company filed on a 10-K, in the currency its 20-F proves mis-signed: whether
        // the issuer is read is decided on its whole history, so the statement and the fact
        // series both leave the unchecked year out.
        await Seed();
        const string annual2022 = "0001213900-23-020001";
        DateOnly filed2022 = new(2023, 3, 30);
        AddYear(_picpay, _owners, 2022, 12_345_000m, annual2022, filed2022, DocumentType.TenK);
        AddYear(_picpay, _total, 2022, 12_345_000m, annual2022, filed2022, DocumentType.TenK);
        AddYear(_picpay, _revenue, 2022, 4_000_000_000m, annual2022, filed2022, DocumentType.TenK);
        await DbContext.SaveChangesAsync();

        var statement = await StatementSut()
            .GetFinancialStatement("PICS", statement: "income", year: 2022, period: "FY");
        var series = await FactsSut().GetFinancialFact("PICS", "net-income");

        statement.Should().Contain("4,000,000,000");
        statement.Should().NotContain("12,345,000");
        statement.Should().Contain(OwnersProfitSign.WithheldNote);
        series.Should().NotContain("12,345,000");
    }

    [Fact]
    public async Task GetFinancialStatement_QuarterDerivedFromNegatedFigures_CarriesTheProvenSign()
    {
        // The first quarter's and the half year's 6-Ks each prove their tag mis-signed; the second
        // quarter is the half year less the first quarter, read after both are negated.
        await Seed();
        const string firstQuarter = "0001213900-26-060001";
        const string halfYear = "0001213900-26-093207";
        DateOnly start = new(2026, 1, 1),
            march = new(2026, 3, 31),
            june = new(2026, 6, 30);
        DateOnly filedMay = new(2026, 5, 20),
            filedAugust = new(2026, 8, 25);
        AddSpan(
            _picpay,
            _owners,
            start,
            march,
            SecFiscalPeriod.Q1,
            -140_000_000m,
            firstQuarter,
            filedMay,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _total,
            start,
            march,
            SecFiscalPeriod.Q1,
            150_000_000m,
            firstQuarter,
            filedMay,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _minority,
            start,
            march,
            SecFiscalPeriod.Q1,
            10_000_000m,
            firstQuarter,
            filedMay,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _revenue,
            start,
            march,
            SecFiscalPeriod.Q1,
            2_000_000_000m,
            firstQuarter,
            filedMay,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _owners,
            start,
            june,
            SecFiscalPeriod.Q2,
            -421_616_000m,
            halfYear,
            filedAugust,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _total,
            start,
            june,
            SecFiscalPeriod.Q2,
            420_765_000m,
            halfYear,
            filedAugust,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _minority,
            start,
            june,
            SecFiscalPeriod.Q2,
            -851_000m,
            halfYear,
            filedAugust,
            DocumentType.SixK
        );
        AddSpan(
            _picpay,
            _revenue,
            start,
            june,
            SecFiscalPeriod.Q2,
            4_500_000_000m,
            halfYear,
            filedAugust,
            DocumentType.SixK
        );
        await DbContext.SaveChangesAsync();

        var result = await StatementSut()
            .GetFinancialStatement("PICS", statement: "income", year: 2026, period: "Q2");

        result.Should().Contain("| Net Income | 281,616,000 | BRL | Derived quarter |");
        result.Should().Contain(OwnersProfitSign.NegatedNote);
    }
}
