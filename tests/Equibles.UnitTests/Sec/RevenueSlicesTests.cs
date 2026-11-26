using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Figures in millions from the filings named in each case: a slice of the top line leaves the
/// revenue facts, a line that adds up inside its own year stays.
/// </summary>
public class RevenueSlicesTests
{
    private static readonly Guid Revenues = Guid.NewGuid();
    private static readonly Guid ContractRevenue = Guid.NewGuid();
    private static readonly Guid ContractRevenueWithTax = Guid.NewGuid();

    private const string AnnualFiling = "0000950170-25-026584";
    private const string QuarterFiling = "0000950170-24-122256";

    private static readonly DateOnly YearStart = new(2024, 1, 1);

    private static FinancialFact Fact(
        Guid concept,
        decimal millions,
        string accession,
        DateOnly end,
        DocumentType form
    ) =>
        new()
        {
            FinancialConceptId = concept,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = YearStart,
            PeriodEnd = end,
            Value = millions * 1_000_000m,
            FiscalYear = 2024,
            FiscalPeriod =
                end.Month == 12 ? SecFiscalPeriod.FullYear
                : end.Month == 3 ? SecFiscalPeriod.Q1
                : SecFiscalPeriod.Q3,
            Form = form,
            FiledDate =
                form == DocumentType.TenK ? new DateOnly(2025, 2, 25) : new DateOnly(2024, 11, 6),
            AccessionNumber = accession,
        };

    private static FinancialFact Year(
        Guid concept,
        decimal millions,
        string accession = AnnualFiling
    ) => Fact(concept, millions, accession, new DateOnly(2024, 12, 31), DocumentType.TenK);

    private static FinancialFact NineMonths(Guid concept, decimal millions) =>
        Fact(concept, millions, QuarterFiling, new DateOnly(2024, 9, 30), DocumentType.TenQ);

    private static FinancialFact FirstQuarter(Guid concept, decimal millions) =>
        Fact(concept, millions, QuarterFiling, new DateOnly(2024, 3, 31), DocumentType.TenQ);

    // BlackRock fiscal 2024: Revenues states a part of the 20,407M its contract revenue states,
    // whose nine months already came to 14,730M.
    [Fact]
    public void Without_YearBelowAnotherTagsYearThatStatesItsNineMonths_DropsTheYear()
    {
        var slice = Year(Revenues, 12_794m);
        var whole = Year(ContractRevenue, 20_407m);
        var nineMonths = NineMonths(ContractRevenue, 14_730m);

        var kept = RevenueSlices.Without([slice, whole, nineMonths]);

        kept.Should().BeEquivalentTo([whole, nineMonths], o => o.WithStrictOrdering());
    }

    // Philip Morris fiscal 2022: revenue net of excise taxes is 31,762M against 80,669M with them,
    // and its own nine months (23,610M) add up inside its year.
    [Fact]
    public void Without_YearItsOwnNineMonthsAddUpInside_KeepsTheYear()
    {
        var facts = new[]
        {
            Year(ContractRevenue, 31_762m),
            NineMonths(ContractRevenue, 23_610m),
            Year(ContractRevenueWithTax, 80_669m),
            NineMonths(ContractRevenueWithTax, 60_638m),
        };

        RevenueSlices.Without(facts).Should().HaveCount(4);
    }

    // Flowserve tags Revenues 0 beside its contract revenue in every filing, quarters included.
    [Fact]
    public void Without_ZeroBesideAnotherTagsPositiveFigure_DropsTheZero()
    {
        var zeroQuarter = FirstQuarter(Revenues, 0m);
        var quarter = FirstQuarter(ContractRevenue, 1_087.5m);
        var zeroYear = Year(Revenues, 0m);
        var year = Year(ContractRevenue, 4_557.8m);

        var kept = RevenueSlices.Without([zeroQuarter, quarter, zeroYear, year]);

        kept.Should().BeEquivalentTo([quarter, year], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Without_ZeroNoOtherTagStates_KeepsIt()
    {
        var zero = Year(Revenues, 0m);

        RevenueSlices.Without([zero]).Should().ContainSingle().Which.Should().BeSameAs(zero);
    }

    // A later 10-K restating the year for a spin-off states it alone, below the nine months filed
    // before the spin-off: the filing contradicts nothing, so the restated year stands.
    [Fact]
    public void Without_RestatedYearItsFilingStatesAlone_KeepsTheYear()
    {
        var restated = Year(Revenues, 29_139m, "0000040545-25-000011");
        var facts = new[]
        {
            restated,
            NineMonths(Revenues, 54_769m),
            Year(ContractRevenue, 58_100m, "0000040545-24-000014"),
            NineMonths(ContractRevenue, 42_000m),
        };

        RevenueSlices.Without(facts).Should().Contain(restated).And.HaveCount(4);
    }

    // A filer that reports half years states no nine months, so no tag's quarters prove the year.
    [Fact]
    public void Without_OtherTagStatesNoNineMonths_KeepsTheYear()
    {
        var facts = new[] { Year(Revenues, 25_209m), Year(ContractRevenue, 53_845m) };

        RevenueSlices.Without(facts).Should().HaveCount(2);
    }

    // NetApp fiscal 2024: 5,657M against 6,268M is within a quarter's revenue of the other year.
    [Fact]
    public void Without_YearWithinAQuarterOfAnotherTagsYear_KeepsTheYear()
    {
        var facts = new[]
        {
            Year(Revenues, 5_657m),
            Year(ContractRevenue, 6_268m),
            NineMonths(ContractRevenue, 4_680m),
        };

        RevenueSlices.Without(facts).Should().HaveCount(3);
    }

    // The chart reads the facts without slices, then folds them, which drops slices again: a zero
    // nine months beside the contract figure supports no year in either call.
    [Fact]
    public void Without_ZeroNineMonthsBesideAYearSlice_DropsBothInOneCallAndASecondDropsNothing()
    {
        var zeroNineMonths = NineMonths(Revenues, 0m);
        var slice = Year(Revenues, 12_794m);
        var whole = Year(ContractRevenue, 20_407m);
        var nineMonths = NineMonths(ContractRevenue, 14_730m);

        var once = RevenueSlices.Without([zeroNineMonths, slice, whole, nineMonths]);

        once.Should().BeEquivalentTo([whole, nineMonths], o => o.WithStrictOrdering());
        RevenueSlices.Without(once).Should().BeEquivalentTo(once, o => o.WithStrictOrdering());
    }

    // A commodity pool's Revenues nets its trading losses: a negative year is no slice.
    [Fact]
    public void Without_NegativeYear_KeepsTheYear()
    {
        var facts = new[]
        {
            Year(Revenues, -34.9m),
            Year(ContractRevenueWithTax, 0.4m),
            NineMonths(ContractRevenueWithTax, 0.3m),
        };

        RevenueSlices.Without(facts).Should().HaveCount(3);
    }
}
