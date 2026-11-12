using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The revenue alias folds in a bank's net interest plus noninterest income. Figures are Zions'
/// filings in millions: its Revenues tag is a fee slice (Q2 2026: 182 against 677 + 460), so the
/// sum outranks it; a line equal to the sum, or a broader line above it, keeps every period; a
/// line that states the sum on another basis gives way to it; a period only one component states
/// keeps revenue; a bank with no revenue tag reads the sum.
/// </summary>
public class BankTopLineFoldTests
{
    private static readonly Guid Revenues = Guid.NewGuid();
    private static readonly Guid ContractRevenue = Guid.NewGuid();
    private static readonly Guid NetInterest = Guid.NewGuid();
    private static readonly Guid Noninterest = Guid.NewGuid();

    private static readonly Dictionary<Guid, int> RevenuePriority = new()
    {
        [Revenues] = 0,
        [ContractRevenue] = 3,
    };

    private const string Q1Filing = "0000109380-26-000083";
    private const string Q2Filing = "0000109380-26-000111";
    private const string AnnualFiling = "0000109380-26-000046";

    private static FinancialFact Fact(
        Guid concept,
        decimal millions,
        string accession,
        SecFiscalPeriod period,
        DateOnly start,
        DateOnly end,
        DateOnly filed
    ) =>
        new()
        {
            FinancialConceptId = concept,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = millions * 1_000_000m,
            FiscalYear = period == SecFiscalPeriod.FullYear ? 2025 : 2026,
            FiscalPeriod = period,
            Form = period == SecFiscalPeriod.FullYear ? DocumentType.TenK : DocumentType.TenQ,
            FiledDate = filed,
            AccessionNumber = accession,
        };

    private static FinancialFact Q1(Guid concept, decimal millions) =>
        Fact(
            concept,
            millions,
            Q1Filing,
            SecFiscalPeriod.Q1,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 31),
            new DateOnly(2026, 5, 7)
        );

    private static FinancialFact Q2(Guid concept, decimal millions) =>
        Fact(
            concept,
            millions,
            Q2Filing,
            SecFiscalPeriod.Q2,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 8, 6)
        );

    private static FinancialFact Year(Guid concept, decimal millions) =>
        Fact(
            concept,
            millions,
            AnnualFiling,
            SecFiscalPeriod.FullYear,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            new DateOnly(2026, 2, 24)
        );

    // The per-period pick every series applies after the fold.
    private static Dictionary<SecFiscalPeriod, decimal> PickMillions(
        IReadOnlyCollection<FinancialFact> revenue,
        IReadOnlyCollection<FinancialFact> netInterest,
        IReadOnlyCollection<FinancialFact> noninterest
    )
    {
        var (facts, priority) = BankTopLine.Fold(
            revenue,
            netInterest,
            noninterest,
            RevenuePriority
        );
        return facts
            .GroupBy(f => (f.FiscalYear, f.FiscalPeriod))
            .Select(g => FinancialFactSeriesShaper.PickBestFact(g, priority))
            .ToDictionary(f => f.FiscalPeriod, f => f.Value / 1_000_000m);
    }

    [Fact]
    public void Fold_RevenuesIsAFeeSlice_EveryPeriodReadsTheBankSum()
    {
        var picked = PickMillions(
            [Q1(Revenues, 172m), Q2(Revenues, 182m), Year(Revenues, 662m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m), Year(NetInterest, 2_627m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m), Year(Noninterest, 758m)]
        );

        picked
            .Should()
            .BeEquivalentTo(
                new Dictionary<SecFiscalPeriod, decimal>
                {
                    [SecFiscalPeriod.Q1] = 849m,
                    [SecFiscalPeriod.Q2] = 1_137m,
                    [SecFiscalPeriod.FullYear] = 3_385m,
                }
            );
    }

    [Fact]
    public void Fold_RevenuesEqualsTheSum_KeepsTheFilersOwnFacts()
    {
        var (facts, _) = BankTopLine.Fold(
            [Q1(Revenues, 849m), Q2(Revenues, 1_137m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m)],
            RevenuePriority
        );

        facts.Should().OnlyContain(fact => fact.FinancialConceptId == Revenues);
    }

    [Fact]
    public void Fold_RevenuesIsABroaderLine_KeepsRevenues()
    {
        // Revenue before interest expense sits well above net interest plus noninterest income.
        var picked = PickMillions(
            [Q1(Revenues, 1_150m), Q2(Revenues, 1_480m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m)]
        );

        picked[SecFiscalPeriod.Q1].Should().Be(1_150m);
        picked[SecFiscalPeriod.Q2].Should().Be(1_480m);
    }

    [Fact]
    public void Fold_RevenueBeforeInterestExpenseNearsTheSumWhenRatesAreLow_KeepsRevenues()
    {
        // Raymond James tags revenue before interest expense. Its newest quarter here is the one
        // ended 2020-12-31 (2,260 against 165 + 2,057, 1.7% above); the year is its fiscal 2023
        // (12,992 against 2,375 + 9,244), which proves the line a broader one.
        var picked = PickMillions(
            [Q2(Revenues, 2_260m), Year(Revenues, 12_992m)],
            [Q2(NetInterest, 165m), Year(NetInterest, 2_375m)],
            [Q2(Noninterest, 2_057m), Year(Noninterest, 9_244m)]
        );

        picked[SecFiscalPeriod.Q2].Should().Be(2_260m);
        picked[SecFiscalPeriod.FullYear].Should().Be(12_992m);
    }

    [Fact]
    public void Fold_RevenuesIsTheTaxableEquivalentTotal_EveryPeriodReadsTheBankSum()
    {
        // US Bancorp tags its taxable-equivalent total net revenue as Revenues, about half a
        // percent above the GAAP components beside it (FY2025: 28,656 against 16,649 + 11,891).
        var picked = PickMillions(
            [Q1(Revenues, 7_288m), Q2(Revenues, 7_712m), Year(Revenues, 28_656m)],
            [Q1(NetInterest, 4_263m), Q2(NetInterest, 4_361m), Year(NetInterest, 16_649m)],
            [Q1(Noninterest, 2_997m), Q2(Noninterest, 3_325m), Year(Noninterest, 11_891m)]
        );

        picked
            .Should()
            .BeEquivalentTo(
                new Dictionary<SecFiscalPeriod, decimal>
                {
                    [SecFiscalPeriod.Q1] = 7_260m,
                    [SecFiscalPeriod.Q2] = 7_686m,
                    [SecFiscalPeriod.FullYear] = 28_540m,
                }
            );
    }

    [Fact]
    public void Fold_RevenuesLeavesALineOutOfTheSum_EveryPeriodReadsTheBankSum()
    {
        // A total stated after the credit-loss provision sits a few percent under the sum.
        var picked = PickMillions(
            [Q1(Revenues, 840m), Q2(Revenues, 1_100m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m)]
        );

        picked[SecFiscalPeriod.Q1].Should().Be(849m);
        picked[SecFiscalPeriod.Q2].Should().Be(1_137m);
    }

    [Fact]
    public void Fold_NewestPeriodLacksAComponent_KeepsRevenuesEverywhere()
    {
        // Without a newest-period sum to prove the line partial, no history splices onto it.
        var picked = PickMillions(
            [Q1(Revenues, 172m), Q2(Revenues, 182m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m)]
        );

        picked[SecFiscalPeriod.Q1].Should().Be(172m);
        picked[SecFiscalPeriod.Q2].Should().Be(182m);
    }

    [Fact]
    public void Fold_NoRevenueTag_FillsFromTheBankSum()
    {
        var picked = PickMillions(
            [],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m)]
        );

        picked[SecFiscalPeriod.Q1].Should().Be(849m);
        picked[SecFiscalPeriod.Q2].Should().Be(1_137m);
    }

    [Fact]
    public void Fold_HeadlineYear_IsNeverFilledFromTheSum()
    {
        // A year that reports its own broader line keeps one measure, so a quarter it leaves
        // untagged stays empty rather than reading the other measure.
        var picked = PickMillions(
            [Q2(Revenues, 1_480m)],
            [Q1(NetInterest, 662m), Q2(NetInterest, 677m)],
            [Q1(Noninterest, 187m), Q2(Noninterest, 460m)]
        );

        picked.Should().NotContainKey(SecFiscalPeriod.Q1);
        picked[SecFiscalPeriod.Q2].Should().Be(1_480m);
    }

    [Fact]
    public void Fold_YearWithoutRevenue_FillsFromTheSum()
    {
        var (facts, priority) = BankTopLine.Fold(
            [Year(Revenues, 4_400m)],
            [Year(NetInterest, 2_627m), Q1(NetInterest, 662m)],
            [Year(Noninterest, 758m), Q1(Noninterest, 187m)],
            RevenuePriority
        );

        facts
            .Select(f => (f.FiscalYear, f.Value / 1_000_000m))
            .Should()
            .BeEquivalentTo(new[] { (2025, 4_400m), (2026, 849m) });
    }

    [Fact]
    public void Fold_PartialRevenues_LeavesNoRevenueFactInAYearTheSumReaches()
    {
        // Zions' FY2025 10-K tags no fourth quarter for Revenues; a fourth quarter derived as the
        // year less its first three quarters must read one measure throughout.
        var (facts, _) = BankTopLine.Fold(
            [Year(Revenues, 662m), Q1(Revenues, 172m)],
            [Year(NetInterest, 2_627m), Q1(NetInterest, 662m)],
            [Year(Noninterest, 758m), Q1(Noninterest, 187m)],
            RevenuePriority
        );

        facts.Should().OnlyContain(f => f.FinancialConceptId == BankTopLine.ConceptId);
    }

    [Fact]
    public void Fold_ContractTagIsTheNewestSlice_TheHeadlineDecides()
    {
        // A fee-only contract tag beside the broader Revenues line is never the line being tested.
        var picked = PickMillions(
            [Q2(Revenues, 1_480m), Q2(ContractRevenue, 182m)],
            [Q2(NetInterest, 677m)],
            [Q2(Noninterest, 460m)]
        );

        picked[SecFiscalPeriod.Q2].Should().Be(1_480m);
    }

    [Fact]
    public void Fold_ComponentsFromDifferentFilings_DoNotPair()
    {
        // Each sum is one filing's two components for one period.
        var restatedNoninterest = Q1(Noninterest, 187m);
        restatedNoninterest.AccessionNumber = Q2Filing;

        var picked = PickMillions(
            [Q1(Revenues, 172m)],
            [Q1(NetInterest, 662m)],
            [restatedNoninterest]
        );

        picked[SecFiscalPeriod.Q1].Should().Be(172m);
    }

    [Fact]
    public void Fold_SummedFact_CarriesTheBankConceptAndTheFiling()
    {
        var (facts, _) = BankTopLine.Fold(
            [Q2(Revenues, 182m)],
            [Q2(NetInterest, 677m)],
            [Q2(Noninterest, 460m)],
            RevenuePriority
        );

        var sum = facts.Single(f => f.FinancialConceptId == BankTopLine.ConceptId);
        sum.AccessionNumber.Should().Be(Q2Filing);
        sum.Form.Should().Be(DocumentType.TenQ);
        sum.FiledDate.Should().Be(new DateOnly(2026, 8, 6));
        sum.PeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        sum.PeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Fold_NonPositiveSum_IsNoTopLine()
    {
        var picked = PickMillions([], [Q2(NetInterest, -40m)], [Q2(Noninterest, 10m)]);

        picked.Should().BeEmpty();
    }
}
