using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.TestSupport;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Figures an earnings release states for a period no periodic report covers yet. The tables are
/// CareTrust REIT's release of 5 November 2026 (thousands); the facts on file are its third
/// quarter and nine months of 2025 and its 2025 year-end balance sheet, as its 10-Q and 10-K
/// reported them, under the labels those reports gave each line.
/// </summary>
public class ReleaseStatementMatcherTests
{
    private const string IncomeStatement = CareTrustRelease.IncomeStatement;
    private const string BalanceSheet = CareTrustRelease.BalanceSheet;

    private static readonly DateOnly ReleaseFiled = new(2026, 11, 5);
    private static readonly DateOnly Q3Start2025 = new(2025, 7, 1);
    private static readonly DateOnly YearStart2025 = new(2025, 1, 1);
    private static readonly DateOnly Q3End2025 = new(2025, 9, 30);
    private static readonly DateOnly YearEnd2025 = new(2025, 12, 31);
    private static readonly DateOnly Q3Start2026 = new(2026, 7, 1);
    private static readonly DateOnly YearStart2026 = new(2026, 1, 1);
    private static readonly DateOnly Q3End2026 = new(2026, 9, 30);

    private static readonly (
        string Tag,
        string Label,
        decimal Quarter,
        decimal NineMonths
    )[] Flows = CareTrustRelease.Flows;

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

        foreach (var (tag, label, value) in CareTrustRelease.Balances)
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
