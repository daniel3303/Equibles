using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Figures an earnings release states for a period no periodic report covers yet. The tables are
/// CareTrust REIT's release of 5 November 2026 (thousands); the facts on file are its third
/// quarter and nine months of 2025 and its 2025 year-end balance sheet, as its 10-Q and 10-K
/// reported them, under the labels those reports gave each line.
/// </summary>
public class ReleaseStatementMatcherTests
{
    private const string IncomeStatement = """
        | CARETRUST REIT, INC. |  |  |  |  |  |
        | --- | --- | --- | --- | --- | --- |
        | CONSOLIDATED INCOME STATEMENTS |  |  |  |  |  |
        | (in thousands, except per share data) |  |  |  |  |  |
        | (Unaudited) |  |  |  |  |  |
        |  |  | For the Three Months EndedSeptember 30, |  | For the Nine Months EndedSeptember 30, |  |
        |  |  | 2026 | 2025 | 2026 | 2025 |
        | Revenues: |  |  |  |  |  |
        |  | Rental income | 127,236 | 104,265 | 359,637 | 261,944 |
        |  | Resident fees and services | 7,226 | — | 15,721 | — |
        |  | Interest income from financing receivables | 14,030 | 2,908 | 28,504 | 8,601 |
        |  | Interest income from other real estate related investments and other income | 27,868 | 25,271 | 76,629 | 70,989 |
        |  | Total revenues | 176,360 | 132,444 | 480,491 | 341,534 |
        | Expenses: |  |  |  |  |  |
        |  | Depreciation and amortization | 33,258 | 26,693 | 93,050 | 65,749 |
        |  | Interest expense | 17,716 | 12,622 | 44,282 | 32,329 |
        |  | Property taxes and insurance | 2,852 | 2,326 | 7,468 | 6,508 |
        |  | Senior housing operating expenses | 5,852 | — | 12,690 | — |
        |  | Transaction costs | 8,933 | 560 | 9,492 | 1,509 |
        |  | Property operating (recoveries) expenses | (108) | 279 | 184 | 1,322 |
        |  | General and administrative | 15,615 | 15,420 | 45,729 | 36,992 |
        |  | Total expenses | 84,118 | 58,352 | 217,566 | 144,861 |
        | Other (loss) income: |  |  |  |  |  |
        |  | Total other (loss) income | (7,139) | 2,915 | (5,275) | 14,459 |
        | Income before income tax expense |  | 85,103 | 77,007 | 257,650 | 211,132 |
        |  | Income tax expense | (2,929) | (2,077) | (7,735) | (3,107) |
        | Net income |  | 82,174 | 74,930 | 249,915 | 208,025 |
        |  | Net (loss) income attributable to noncontrolling interests | (1,479) | 29 | (2,944) | (1,223) |
        | Net income attributable to CareTrust REIT, Inc. |  | 83,653 | 74,901 | 252,859 | 209,248 |
        """;

    private const string BalanceSheet = """
        | CARETRUST REIT, INC. |  |  |
        | --- | --- | --- |
        | CONSOLIDATED BALANCE SHEETS |  |  |
        | (in thousands) |  |  |
        | (Unaudited) |  |  |
        |  | September 30, 2026 | December 31, 2025 |
        | Assets: |  |  |
        | Real estate investments, net | 4,717,309 | 3,709,576 |
        | Cash and cash equivalents | 479,876 | 198,042 |
        | Accounts and other receivables | 23,326 | 10,368 |
        | Prepaid expenses and other assets, net | 818,347 | 230,427 |
        | Deferred financing costs, net | 6,484 | 8,568 |
        | Total assets | 7,706,317 | 5,148,436 |
        | Liabilities and Equity: |  |  |
        | Senior unsecured notes payable, net | 398,482 | 397,816 |
        | Senior unsecured term loan, net | 497,015 | 496,404 |
        | Accounts payable, accrued liabilities and deferred rent liabilities | 165,213 | 120,442 |
        | Dividends and distributions payable | 100,606 | 74,806 |
        | Total liabilities | 2,336,316 | 1,089,468 |
        | Redeemable noncontrolling interests | 30,300 | 18,156 |
        | Common stock | 2,575 | 2,227 |
        | Additional paid-in capital | 5,850,070 | 4,518,977 |
        | Total stockholders' equity | 5,330,388 | 4,035,280 |
        | Total equity | 5,339,701 | 4,040,812 |
        | Total liabilities and equity | 7,706,317 | 5,148,436 |
        """;

    private static readonly DateOnly ReleaseFiled = new(2026, 11, 5);
    private static readonly DateOnly Q3Start2025 = new(2025, 7, 1);
    private static readonly DateOnly YearStart2025 = new(2025, 1, 1);
    private static readonly DateOnly Q3End2025 = new(2025, 9, 30);
    private static readonly DateOnly YearEnd2025 = new(2025, 12, 31);
    private static readonly DateOnly Q3Start2026 = new(2026, 7, 1);
    private static readonly DateOnly YearStart2026 = new(2026, 1, 1);
    private static readonly DateOnly Q3End2026 = new(2026, 9, 30);

    // Each income statement line as the 10-Q of the third quarter of 2025 reported it: tag,
    // the label on the statement's face, the quarter and the nine months in thousands.
    private static readonly (
        string Tag,
        string Label,
        decimal Quarter,
        decimal NineMonths
    )[] Flows =
    [
        ("OperatingLeaseLeaseIncome", "Rental income", 104_265, 261_944),
        (
            "ctre:InterestIncomeFinancingReceivables",
            "Interest income from financing receivables",
            2_908,
            8_601
        ),
        (
            "ctre:InterestAndOtherIncome",
            "Interest income from other real estate related investments and other income",
            25_271,
            70_989
        ),
        ("Revenues", "Total revenues", 132_444, 341_534),
        ("DepreciationAndAmortization", "Depreciation and amortization", 26_693, 65_749),
        ("InterestExpense", "Interest expense", 12_622, 32_329),
        ("ctre:PropertyTaxesAndInsurance", "Property taxes and insurance", 2_326, 6_508),
        ("ctre:TransactionCosts", "Transaction costs", 560, 1_509),
        ("GeneralAndAdministrativeExpense", "General and administrative", 15_420, 36_992),
        ("CostsAndExpenses", "Total expenses", 58_352, 144_861),
        ("NonoperatingIncomeExpense", "Total other (loss) income", 2_915, 14_459),
        (
            "IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest",
            "Income before income tax expense",
            77_007,
            211_132
        ),
        ("IncomeTaxExpenseBenefit", "Income tax expense", 2_077, 3_107),
        ("ProfitLoss", "Net income", 74_930, 208_025),
        ("NetIncomeLoss", "Net income attributable to CareTrust REIT, Inc.", 74_901, 209_248),
    ];

    // The 2025 year-end balance sheet as the 10-K reported it.
    private static readonly (string Tag, string Label, decimal Value)[] Balances =
    [
        ("RealEstateInvestmentPropertyNet", "Real estate investments, net", 3_709_576),
        ("CashAndCashEquivalentsAtCarryingValue", "Cash and cash equivalents", 198_042),
        ("ctre:AccountsAndOtherReceivables", "Accounts and other receivables", 10_368),
        ("ctre:PrepaidExpensesAndOtherAssets", "Prepaid expenses and other assets, net", 230_427),
        ("ctre:DeferredFinancingCosts", "Deferred financing costs, net", 8_568),
        ("Assets", "Total assets", 5_148_436),
        ("SeniorNotes", "Senior unsecured notes payable, net", 397_816),
        ("ctre:TermLoan", "Senior unsecured term loan, net", 496_404),
        (
            "ctre:AccountsPayableAndAccruedLiabilities",
            "Accounts payable, accrued liabilities and deferred rent liabilities",
            120_442
        ),
        ("DividendsPayableCurrentAndNoncurrent", "Dividends and distributions payable", 74_806),
        ("Liabilities", "Total liabilities", 1_089_468),
        (
            "RedeemableNoncontrollingInterestEquityCarryingAmount",
            "Redeemable noncontrolling interests",
            18_156
        ),
        ("CommonStockValue", "Common stock", 2_227),
        ("AdditionalPaidInCapital", "Additional paid-in capital", 4_518_977),
        ("StockholdersEquity", "Total stockholders' equity", 4_035_280),
        (
            "StockholdersEquityIncludingPortionAttributableToNoncontrollingInterest",
            "Total equity",
            4_040_812
        ),
        ("LiabilitiesAndStockholdersEquity", "Total liabilities and equity", 5_148_436),
    ];

    private readonly Dictionary<string, Guid> _concepts = [];
    private readonly List<OnFileFact> _facts = [];
    private readonly List<FiledStatementLine> _lines = [];

    public ReleaseStatementMatcherTests()
    {
        foreach (var (tag, label, quarter, nineMonths) in Flows)
        {
            AddFact(tag, Q3Start2025, Q3End2025, quarter * 1_000m, SecFiscalPeriod.Q3);
            AddFact(tag, YearStart2025, Q3End2025, nineMonths * 1_000m, SecFiscalPeriod.Q3);
            AddLine(tag, label);
        }

        foreach (var (tag, label, value) in Balances)
        {
            AddFact(tag, YearEnd2025, YearEnd2025, value * 1_000m, SecFiscalPeriod.FullYear);
            AddLine(tag, label);
        }

        // The second quarter of 2026 is the latest period a 10-Q covers.
        AddFact(
            "Revenues",
            new(2026, 4, 1),
            new(2026, 6, 30),
            160_000_000m,
            SecFiscalPeriod.Q2,
            2026
        );
    }

    [Fact]
    public void Match_IncomeStatement_PlacesTheQuarterAndNineMonthsFromTheirComparatives()
    {
        var figures = Match(IncomeStatement);

        Figure(figures, "Revenues", Q3Start2026, Q3End2026)
            .Should()
            .BeEquivalentTo(
                new
                {
                    Value = 176_360_000m,
                    Unit = "USD",
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    SourceLabel = "Total revenues",
                    SourceLine = 13,
                }
            );
        Figure(figures, "Revenues", YearStart2026, Q3End2026).Value.Should().Be(480_491_000m);
        Figure(figures, "NetIncomeLoss", Q3Start2026, Q3End2026).Value.Should().Be(83_653_000m);
        Figure(figures, "ProfitLoss", YearStart2026, Q3End2026).Value.Should().Be(249_915_000m);
    }

    [Fact]
    public void Match_LineOutsideTheStandardCatalog_IsNotPlaced()
    {
        Match(IncomeStatement)
            .Should()
            .NotContain(f => f.ConceptId == _concepts["ctre:TransactionCosts"]);
    }

    [Fact]
    public void Match_ExpensePrintedAsNegative_KeepsTheSignOnFile()
    {
        var figures = Match(IncomeStatement);

        Figure(figures, "IncomeTaxExpenseBenefit", Q3Start2026, Q3End2026)
            .Value.Should()
            .Be(2_929_000m);
    }

    [Fact]
    public void Match_SignDiffersOnALineWithoutADebitOrCreditSide_LeavesTheLineOut()
    {
        ReplaceFacts("IncomeTaxExpenseBenefit", f => f with { Balance = null });

        Match(IncomeStatement)
            .Should()
            .NotContain(f => f.ConceptId == _concepts["IncomeTaxExpenseBenefit"]);
    }

    [Fact]
    public void Match_LabelTheFilerNeverGaveTheLine_LeavesTheRowOut()
    {
        _lines.RemoveAll(l => l.Tag == "Revenues");
        AddLine("Revenues", "Total net revenues");

        var figures = Match(IncomeStatement);

        figures.Should().NotContain(f => f.ConceptId == _concepts["Revenues"]);
        Figure(figures, "ProfitLoss", Q3Start2026, Q3End2026).Value.Should().Be(82_174_000m);
    }

    [Fact]
    public void Match_TwoRowsEqualOneFactAndPartThisYear_LeavesTheFigureOut()
    {
        // Last year the company had no noncontrolling interest: one net income fact on file,
        // labelled "Net income", which both rows of the release now equal in the 2025 columns.
        _facts.RemoveAll(f => f.Tag is "ProfitLoss" or "NetIncomeLoss");
        _lines.RemoveAll(l => l.Tag is "ProfitLoss" or "NetIncomeLoss");
        AddFact("NetIncomeLoss", Q3Start2025, Q3End2025, 74_930_000m, SecFiscalPeriod.Q3);
        AddFact("NetIncomeLoss", YearStart2025, Q3End2025, 208_025_000m, SecFiscalPeriod.Q3);
        AddLine("NetIncomeLoss", "Net income");
        Figure(Match(IncomeStatement), "NetIncomeLoss", Q3Start2026, Q3End2026)
            .Value.Should()
            .Be(82_174_000m);
        var release = IncomeStatement.Replace(
            "|  | 83,653 | 74,901 | 252,859 | 209,248 |",
            "|  | 83,653 | 74,930 | 252,859 | 208,025 |"
        );

        Match(release).Should().NotContain(f => f.ConceptId == _concepts["NetIncomeLoss"]);
    }

    [Fact]
    public void Match_PeriodOnFileAtTwoValues_IsNoComparative()
    {
        AddFact("Revenues", Q3Start2025, Q3End2025, 130_000_000m, SecFiscalPeriod.Q3);

        var figures = Match(IncomeStatement);

        figures
            .Should()
            .NotContain(f => f.ConceptId == _concepts["Revenues"] && f.PeriodStart == Q3Start2026);
        Figure(figures, "Revenues", YearStart2026, Q3End2026).Value.Should().Be(480_491_000m);
    }

    [Fact]
    public void Match_TableReproducingFewLinesOnFile_PlacesNothing()
    {
        var kept = Flows.Take(6).Select(f => f.Tag).ToHashSet();
        _facts.RemoveAll(f => f.PeriodEnd == Q3End2025 && !kept.Contains(f.Tag));

        Match(IncomeStatement).Should().BeEmpty();
    }

    [Fact]
    public void Match_PeriodicReportAlreadyCoversTheQuarter_PlacesNothing()
    {
        AddFact("Revenues", Q3Start2026, Q3End2026, 176_360_000m, SecFiscalPeriod.Q3, 2026);

        Match(IncomeStatement).Should().BeEmpty();
    }

    [Fact]
    public void Match_BalanceSheet_DatesTheNewColumnFromItsHeaderAndNamesItAfterTheQuarter()
    {
        var figures = Match(IncomeStatement + "\n\n" + BalanceSheet);

        Figure(figures, "Assets", Q3End2026, Q3End2026)
            .Should()
            .BeEquivalentTo(
                new
                {
                    Value = 7_706_317_000m,
                    PeriodType = FactPeriodType.Instant,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    SourceLabel = "Total assets",
                }
            );
        Figure(figures, "LiabilitiesAndStockholdersEquity", Q3End2026, Q3End2026)
            .Value.Should()
            .Be(7_706_317_000m);
        Figure(figures, "CashAndCashEquivalentsAtCarryingValue", Q3End2026, Q3End2026)
            .Value.Should()
            .Be(479_876_000m);
    }

    [Fact]
    public void Match_NoStatementLinesOnFile_PlacesNothing()
    {
        _lines.Clear();

        Match(IncomeStatement).Should().BeEmpty();
    }

    private IReadOnlyList<ReleaseFigure> Match(string release) =>
        ReleaseStatementMatcher.Match(release, ReleaseFiled, _facts, _lines);

    private ReleaseFigure Figure(
        IReadOnlyList<ReleaseFigure> figures,
        string tag,
        DateOnly start,
        DateOnly end
    ) =>
        figures
            .Should()
            .ContainSingle(f =>
                f.ConceptId == _concepts[tag] && f.PeriodStart == start && f.PeriodEnd == end
            )
            .Subject;

    private void AddFact(
        string tag,
        DateOnly start,
        DateOnly end,
        decimal value,
        SecFiscalPeriod period,
        int fiscalYear = 2025
    )
    {
        if (!_concepts.TryGetValue(tag, out var concept))
            _concepts[tag] = concept = Guid.NewGuid();
        _facts.Add(
            new OnFileFact(
                concept,
                Taxonomy(tag),
                tag,
                "USD",
                start,
                end,
                value,
                fiscalYear,
                period,
                ConceptBalance.Debit
            )
        );
    }

    private void AddLine(string tag, string label) =>
        _lines.Add(new FiledStatementLine(Taxonomy(tag), tag, label));

    private void ReplaceFacts(string tag, Func<OnFileFact, OnFileFact> change)
    {
        for (var i = 0; i < _facts.Count; i++)
        {
            if (_facts[i].Tag == tag)
                _facts[i] = change(_facts[i]);
        }
    }

    // A filer's own tag is stored with its prefix under the company-specific taxonomy.
    private static FactTaxonomy Taxonomy(string tag) =>
        tag.Contains(':') ? FactTaxonomy.Custom : FactTaxonomy.UsGaap;
}
