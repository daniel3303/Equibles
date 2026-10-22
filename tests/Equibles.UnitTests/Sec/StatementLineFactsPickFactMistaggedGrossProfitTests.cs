using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The statement Gross Profit line when the filer tagged its operating income as gross profit.
/// Figures are EBS's FY2017 Q3: GrossProfit 47.8M beside OperatingIncomeLoss 47,769,000, while
/// revenue 149.4M less cost of revenue 44,503,000 is 104,897,000.
/// </summary>
public class StatementLineFactsPickFactMistaggedGrossProfitTests
{
    private static StatementLine Line(string alias) =>
        FinancialStatementConcepts
            .For(FinancialStatementType.IncomeStatement)
            .Single(l => l.Alias == alias);

    private static FinancialFact Quarter(decimal value, DateOnly? start = null) =>
        new()
        {
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start ?? new DateOnly(2017, 7, 1),
            PeriodEnd = new DateOnly(2017, 9, 30),
            Value = value,
            FiscalYear = 2017,
            FiscalPeriod = SecFiscalPeriod.Q3,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2018, 11, 2),
            AccessionNumber = "0001367644-18-000040",
            DimensionsKey = "",
        };

    private static (
        Dictionary<(FactTaxonomy, string), Guid> ConceptIdByKey,
        Dictionary<Guid, FinancialFact> Facts
    ) Period(params (string Tag, FinancialFact Fact)[] reported)
    {
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>();
        var facts = new Dictionary<Guid, FinancialFact>();
        foreach (var (tag, fact) in reported)
        {
            var id = Guid.NewGuid();
            conceptIdByKey[(FactTaxonomy.UsGaap, tag)] = id;
            fact.FinancialConceptId = id;
            facts[id] = fact;
        }
        return (conceptIdByKey, facts);
    }

    private static (
        Dictionary<(FactTaxonomy, string), Guid> ConceptIdByKey,
        Dictionary<Guid, FinancialFact> Facts
    ) Ebs(decimal grossProfit = 47_800_000m, DateOnly? incomeStart = null) =>
        Period(
            ("SalesRevenueNet", Quarter(149_400_000m)),
            ("CostOfGoodsAndServicesSold", Quarter(44_503_000m)),
            ("GrossProfit", Quarter(grossProfit)),
            ("OperatingIncomeLoss", Quarter(47_769_000m, incomeStart))
        );

    [Fact]
    public void PickConceptFact_GrossProfitIsTheOperatingIncome_ReadsRevenueLessCost()
    {
        var (conceptIdByKey, facts) = Ebs();

        var (concept, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(104_897_000m);
        StatementQuarterDerivation.IsDerived(fact).Should().BeTrue();
        fact.PeriodStart.Should().Be(new DateOnly(2017, 7, 1));
        concept.Tag.Should().Be("GrossProfit");
        StatementLineFacts
            .ReplacesMistaggedGrossProfit(Line("gross-profit"), conceptIdByKey, facts)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void PickConceptFact_ReplacingNeverTouchesTheStoredFact()
    {
        var (conceptIdByKey, facts) = Ebs();
        var stored = facts[conceptIdByKey[(FactTaxonomy.UsGaap, "GrossProfit")]];

        StatementLineFacts.PickConceptFact(Line("gross-profit"), conceptIdByKey, facts);

        stored.Value.Should().Be(47_800_000m);
        stored.Form.Should().Be(DocumentType.TenQ);
    }

    [Fact]
    public void PickConceptFact_GenuineGrossProfit_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Ebs(grossProfit: 104_897_000m);

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(104_897_000m);
        StatementQuarterDerivation.IsDerived(fact).Should().BeFalse();
        StatementLineFacts
            .ReplacesMistaggedGrossProfit(Line("gross-profit"), conceptIdByKey, facts)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void PickConceptFact_OperatingIncomeOverAnotherSpan_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Ebs(incomeStart: new DateOnly(2017, 1, 1));

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(47_800_000m);
    }

    [Fact]
    public void PickConceptFact_NoCostOfRevenue_KeepsTheTaggedFigure()
    {
        var (conceptIdByKey, facts) = Period(
            ("SalesRevenueNet", Quarter(149_400_000m)),
            ("GrossProfit", Quarter(47_800_000m)),
            ("OperatingIncomeLoss", Quarter(47_769_000m))
        );

        var (_, fact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );

        fact.Value.Should().Be(47_800_000m);
    }

    // EBS FY2016 Q4: a later 10-K restated the year as 105.5M, so the operating income derived
    // for the quarter is 50,907,000 against the tagged 50,929,000.
    private static FinancialFact Q4(decimal value, bool derived = false)
    {
        var fact = Quarter(value, new DateOnly(2016, 10, 1));
        fact.PeriodEnd = new DateOnly(2016, 12, 31);
        fact.FiscalYear = 2016;
        fact.FiscalPeriod = SecFiscalPeriod.Q4;
        if (derived)
        {
            fact.Form = null;
            fact.AccessionNumber = null;
        }
        return fact;
    }

    private static (
        Dictionary<(FactTaxonomy, string), Guid> ConceptIdByKey,
        Dictionary<Guid, FinancialFact> Facts
    ) EbsFourthQuarter() =>
        Period(
            ("SalesRevenueNet", Q4(151_663_000m)),
            ("CostOfGoodsAndServicesSold", Q4(33_274_000m, derived: true)),
            ("GrossProfit", Q4(50_929_000m)),
            ("OperatingIncomeLoss", Q4(50_907_000m, derived: true))
        );

    [Fact]
    public void PickConceptFact_DerivedOperatingIncome_MatchesWithinTheYearsRounding()
    {
        var (conceptIdByKey, facts) = EbsFourthQuarter();

        var (_, exact) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts
        );
        var (_, rounded) = StatementLineFacts.PickConceptFact(
            Line("gross-profit"),
            conceptIdByKey,
            facts,
            derivedOperatingIncomeRounding: 100_000m
        );

        exact.Value.Should().Be(50_929_000m);
        rounded.Value.Should().Be(118_389_000m);
    }

    [Fact]
    public void DerivedOperatingIncomeRounding_IsTheLatestLongerSpansCoarsestRounding()
    {
        var conceptIdByKey = new Dictionary<(FactTaxonomy, string), Guid>
        {
            [(FactTaxonomy.UsGaap, "OperatingIncomeLoss")] = Guid.NewGuid(),
        };
        FinancialFact Span(DateOnly end, decimal value, DateOnly filed)
        {
            var fact = Quarter(value, new DateOnly(2016, 1, 1));
            fact.FinancialConceptId = conceptIdByKey[(FactTaxonomy.UsGaap, "OperatingIncomeLoss")];
            fact.PeriodEnd = end;
            fact.FiledDate = filed;
            return fact;
        }

        StatementLineFacts
            .DerivedOperatingIncomeRounding(
                [
                    Span(new DateOnly(2016, 12, 31), 105_522_000m, new DateOnly(2017, 2, 28)),
                    Span(new DateOnly(2016, 12, 31), 105_500_000m, new DateOnly(2019, 2, 22)),
                    Span(new DateOnly(2016, 9, 30), 54_593_000m, new DateOnly(2016, 11, 9)),
                    // A discrete quarter is what gets derived, never an input.
                    Span(new DateOnly(2016, 3, 31), 21_000_000m, new DateOnly(2017, 5, 5)),
                ],
                conceptIdByKey
            )
            .Should()
            .Be(100_000m);
        StatementLineFacts
            .DerivedOperatingIncomeRounding(
                [Span(new DateOnly(2016, 12, 31), 105_522_000m, new DateOnly(2017, 2, 28))],
                conceptIdByKey
            )
            .Should()
            .Be(1_000m);
    }

    // EBS FY2025: only OperatingCostsAndExpenses is tagged, and its history proves it the
    // operating-expenses line.
    [Fact]
    public void PickConceptFact_ClaimedOperatingCosts_FillTheClaimantAndDropTheirOwnLine()
    {
        var (conceptIdByKey, facts) = Period(("OperatingCostsAndExpenses", Quarter(642_800_000m)));

        var (concept, claimed) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingExpensesAlias),
            conceptIdByKey,
            facts,
            OperatingCostsContinuation.OperatingExpensesAlias
        );
        var (_, own) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingCostsAlias),
            conceptIdByKey,
            facts,
            OperatingCostsContinuation.OperatingExpensesAlias
        );

        claimed.Value.Should().Be(642_800_000m);
        concept.Tag.Should().Be("OperatingCostsAndExpenses");
        own.Should().BeNull();
    }

    [Fact]
    public void PickConceptFact_UnclaimedOperatingCosts_KeepTheirOwnLine()
    {
        var (conceptIdByKey, facts) = Period(("OperatingCostsAndExpenses", Quarter(642_800_000m)));

        var (_, other) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingExpensesAlias),
            conceptIdByKey,
            facts
        );
        var (_, own) = StatementLineFacts.PickConceptFact(
            Line(OperatingCostsContinuation.OperatingCostsAlias),
            conceptIdByKey,
            facts
        );

        other.Should().BeNull();
        own.Value.Should().Be(642_800_000m);
    }
}
