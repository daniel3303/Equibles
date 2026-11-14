using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The depreciation-and-amortization alias reads one total a fiscal year. The filed figures are in
/// millions: McDonald's tags the total beside a slice of it (FY2025: 2,199 against 457), AES the
/// total beside the same charge with the other sign, Entergy the total beside the broader
/// accretion-net figure, and Microsoft no total at all, only depreciation and amortization of
/// intangible assets (FY2026: 34,300 plus 4,700). A fact under the <see cref="Constructed"/>
/// accession is made up for its test.
/// </summary>
public class DepreciationTotalTests
{
    private static readonly Guid DepletionTotal = Guid.NewGuid();
    private static readonly Guid AccretionTotal = Guid.NewGuid();
    private static readonly Guid PlainTotal = Guid.NewGuid();
    private static readonly Guid RespelledPlainTotal = Guid.NewGuid();
    private static readonly Guid Depreciation = Guid.NewGuid();
    private static readonly Guid Amortization = Guid.NewGuid();

    private static readonly DepreciationTotal.Concepts Concepts = new([
        (DepletionTotal, FactTaxonomy.UsGaap, "DepreciationDepletionAndAmortization"),
        (AccretionTotal, FactTaxonomy.UsGaap, "DepreciationAmortizationAndAccretionNet"),
        (PlainTotal, FactTaxonomy.UsGaap, "DepreciationAndAmortization"),
        (RespelledPlainTotal, FactTaxonomy.UsGaap, "DepreciationAndAmortization"),
        (Depreciation, FactTaxonomy.UsGaap, "Depreciation"),
        (Amortization, FactTaxonomy.UsGaap, "AmortizationOfIntangibleAssets"),
    ]);

    // The alias's own order: the depletion total, the accretion-net total, then the plain total.
    private static readonly Dictionary<Guid, int> FamilyPriority = new()
    {
        [DepletionTotal] = 0,
        [AccretionTotal] = 1,
        [PlainTotal] = 2,
        [RespelledPlainTotal] = 2,
    };

    private const string Constructed = "0000000000-00-000000";
    private const string McDonaldsAnnual = "0000063908-26-000035";
    private const string McDonaldsFirstQuarterReport = "0000063908-26-000051";
    private const string McDonaldsHalfYearReport = "0000063908-26-000073";
    private const string MicrosoftAnnual = "0001193125-26-323660";
    private const string MicrosoftFirstQuarterReport = "0001193125-26-436133";

    // The fiscal year is the year the period ends in unless the test states another.
    private static FinancialFact Fact(
        Guid concept,
        decimal millions,
        string start,
        string end,
        string accession,
        string filed,
        int? fiscalYear = null
    )
    {
        var periodStart = DateOnly.Parse(start);
        var periodEnd = DateOnly.Parse(end);
        var annual = periodEnd.DayNumber - periodStart.DayNumber > 300;
        return new FinancialFact
        {
            Id = Guid.NewGuid(),
            FinancialConceptId = concept,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = millions * 1_000_000m,
            FiscalYear = fiscalYear ?? periodEnd.Year,
            FiscalPeriod = annual ? SecFiscalPeriod.FullYear : SecFiscalPeriod.Q1,
            Form = annual ? DocumentType.TenK : DocumentType.TenQ,
            FiledDate = DateOnly.Parse(filed),
            AccessionNumber = accession,
        };
    }

    private static FinancialFact McDonaldsYear(Guid concept, decimal millions) =>
        Fact(concept, millions, "2025-01-01", "2025-12-31", McDonaldsAnnual, "2026-02-24");

    private static FinancialFact McDonaldsFirstQuarter(Guid concept, decimal millions) =>
        Fact(
            concept,
            millions,
            "2026-01-01",
            "2026-03-31",
            McDonaldsFirstQuarterReport,
            "2026-05-07"
        );

    private static FinancialFact McDonaldsHalfYear(Guid concept, decimal millions) =>
        Fact(concept, millions, "2026-01-01", "2026-06-30", McDonaldsHalfYearReport, "2026-08-07");

    // Microsoft's fiscal year runs July to June: FY2026 and the first quarter of FY2027.
    private static readonly FinancialFact[] MicrosoftParts =
    [
        Fact(Depreciation, 34_300m, "2025-07-01", "2026-06-30", MicrosoftAnnual, "2026-07-29"),
        Fact(Amortization, 4_700m, "2025-07-01", "2026-06-30", MicrosoftAnnual, "2026-07-29"),
        Fact(
            Depreciation,
            11_100m,
            "2026-07-01",
            "2026-09-30",
            MicrosoftFirstQuarterReport,
            "2026-10-28",
            fiscalYear: 2027
        ),
        Fact(
            Amortization,
            1_000m,
            "2026-07-01",
            "2026-09-30",
            MicrosoftFirstQuarterReport,
            "2026-10-28",
            fiscalYear: 2027
        ),
    ];

    private static List<decimal> Millions(IEnumerable<FinancialFact> facts, Guid concept) =>
        facts
            .Where(f => f.FinancialConceptId == concept)
            .OrderBy(f => f.PeriodEnd)
            .ThenBy(f => f.PeriodStart)
            .Select(f => f.Value / 1_000_000m)
            .ToList();

    [Fact]
    public void Fold_TotalBesideASlice_LeavesTheSliceOutOfTheYearAndTheQuartersAfterIt()
    {
        var (facts, _) = DepreciationTotal.Fold(
            [
                McDonaldsYear(PlainTotal, 2_199m),
                McDonaldsYear(DepletionTotal, 457m),
                McDonaldsFirstQuarter(PlainTotal, 566m),
                McDonaldsFirstQuarter(DepletionTotal, 111m),
                McDonaldsHalfYear(PlainTotal, 1_131m),
                McDonaldsHalfYear(DepletionTotal, 222m),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        Millions(facts, DepletionTotal).Should().BeEmpty();
        Millions(facts, PlainTotal).Should().Equal(2_199m, 566m, 1_131m);
    }

    [Fact]
    public void Fold_SliceStatesQuartersTheTotalDoesNot_LeavesThemAbsent()
    {
        // The year reads the total, so its quarters never read the slice: a rolling twelve months
        // would add one concept's year to the other's quarters. The total's own quarters are left
        // out of this history on purpose.
        var (facts, _) = DepreciationTotal.Fold(
            [
                McDonaldsYear(PlainTotal, 2_199m),
                McDonaldsYear(DepletionTotal, 457m),
                Fact(DepletionTotal, 121m, "2025-07-01", "2025-09-30", Constructed, "2025-11-05"),
                McDonaldsFirstQuarter(DepletionTotal, 111m),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        Millions(facts, DepletionTotal).Should().BeEmpty();
        Millions(facts, PlainTotal).Should().Equal(2_199m);
    }

    [Fact]
    public void Fold_SliceInOneYearTheOnlyTotalInTheNext_KeepsTheLaterYear()
    {
        // A constructed next year: the concept that was a slice is the only one stating it.
        var (facts, _) = DepreciationTotal.Fold(
            [
                McDonaldsYear(PlainTotal, 2_199m),
                McDonaldsYear(DepletionTotal, 457m),
                Fact(DepletionTotal, 2_260m, "2026-01-01", "2026-12-31", Constructed, "2027-02-23"),
                Fact(DepletionTotal, 566m, "2026-01-01", "2026-03-31", Constructed, "2026-05-07"),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        Millions(facts, DepletionTotal).Should().Equal(566m, 2_260m);
    }

    [Fact]
    public void Fold_RespelledConcept_IsJudgedAsTheTagItRespells()
    {
        // A respelled concept is a separate row with its own id; it is still the plain total.
        var (facts, _) = DepreciationTotal.Fold(
            [McDonaldsYear(RespelledPlainTotal, 2_199m), McDonaldsYear(DepletionTotal, 457m)],
            [],
            Concepts,
            FamilyPriority
        );

        facts.Should().ContainSingle().Which.FinancialConceptId.Should().Be(RespelledPlainTotal);
    }

    [Fact]
    public void Fold_TotalBelowTheAccretionNetFigure_KeepsBoth()
    {
        // Entergy FY2025: the accretion-net figure adds accretion to the same charge, so the
        // total under it is no slice and the alias's own order still picks it.
        const string entergyAnnual = "0000065984-26-000174";
        var (facts, priority) = DepreciationTotal.Fold(
            [
                Fact(
                    DepletionTotal,
                    2_305.6m,
                    "2025-01-01",
                    "2025-12-31",
                    entergyAnnual,
                    "2026-02-19"
                ),
                Fact(
                    AccretionTotal,
                    2_537.1m,
                    "2025-01-01",
                    "2025-12-31",
                    entergyAnnual,
                    "2026-02-19"
                ),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        facts.Should().HaveCount(2);
        FinancialFactSeriesShaper.PickBestFact(facts, priority).Value.Should().Be(2_305_600_000m);
    }

    [Fact]
    public void Fold_AccretionNetWithTheOtherSign_ReadsTheTotal()
    {
        // AES FY2025 tags the charge as 1,457 and, under the accretion-net concept, as -1,457.
        const string aesAnnual = "0000874761-26-000063";
        var (facts, priority) = DepreciationTotal.Fold(
            [
                Fact(AccretionTotal, -1_457m, "2025-01-01", "2025-12-31", aesAnnual, "2026-03-02"),
                Fact(PlainTotal, 1_457m, "2025-01-01", "2025-12-31", aesAnnual, "2026-03-02"),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        FinancialFactSeriesShaper.PickBestFact(facts, priority).Value.Should().Be(1_457_000_000m);
    }

    [Fact]
    public void Fold_TotalWithTheOtherSignBesideAccretionNet_ReadsTheAccretionNetFigure()
    {
        // Constructed: a total that states no charge gives way to the accretion-net figure.
        var (facts, priority) = DepreciationTotal.Fold(
            [
                Fact(DepletionTotal, -900m, "2025-01-01", "2025-12-31", Constructed, "2026-03-02"),
                Fact(AccretionTotal, 900m, "2025-01-01", "2025-12-31", Constructed, "2026-03-02"),
            ],
            [],
            Concepts,
            FamilyPriority
        );

        FinancialFactSeriesShaper.PickBestFact(facts, priority).Value.Should().Be(900_000_000m);
    }

    [Fact]
    public void Fold_NoTotalTagged_SumsDepreciationAndAmortizationOfIntangibles()
    {
        var (facts, priority) = DepreciationTotal.Fold(
            [],
            MicrosoftParts,
            Concepts,
            FamilyPriority
        );

        Millions(facts, DepreciationTotal.ConceptId).Should().Equal(39_000m, 12_100m);
        facts.Should().OnlyContain(fact => DepreciationTotal.IsSum(fact));
        priority[DepreciationTotal.ConceptId].Should().Be(int.MaxValue);
    }

    [Fact]
    public void Fold_YearTheAliasStates_GetsNoSumNorDoesTheOpenYearAfterIt()
    {
        // Constructed: a total tagged for the year the parts state. One measure a fiscal year, so
        // the total stands, and the quarter after it waits for its own annual report, where a
        // total may be tagged again.
        var (facts, _) = DepreciationTotal.Fold(
            [Fact(PlainTotal, 38_534m, "2025-07-01", "2026-06-30", Constructed, "2026-07-29")],
            MicrosoftParts,
            Concepts,
            FamilyPriority
        );

        Millions(facts, PlainTotal).Should().Equal(38_534m);
        Millions(facts, DepreciationTotal.ConceptId).Should().BeEmpty();
    }

    [Fact]
    public void Fold_TotalStoppedAndTheYearClosedOnItsParts_SumsThatYear()
    {
        // Constructed: a total tagged the year before. This year closed with the two parts and no
        // total, so the filer has stopped tagging it: the year reads the sum, and so does the
        // quarter after it.
        var (facts, _) = DepreciationTotal.Fold(
            [Fact(PlainTotal, 34_153m, "2024-07-01", "2025-06-30", Constructed, "2025-07-30")],
            MicrosoftParts,
            Concepts,
            FamilyPriority
        );

        Millions(facts, DepreciationTotal.ConceptId).Should().Equal(39_000m, 12_100m);
    }

    [Fact]
    public void Fold_DepreciationAlone_SumsNothing()
    {
        // A filer that states depreciation and no amortization of intangibles: one part is no total.
        var (facts, priority) = DepreciationTotal.Fold(
            [],
            MicrosoftParts.Where(f => f.FinancialConceptId == Depreciation).ToList(),
            Concepts,
            FamilyPriority
        );

        facts.Should().BeEmpty();
        priority.Should().NotContainKey(DepreciationTotal.ConceptId);
    }

    [Fact]
    public void ForStatement_TotalBesideASlice_DropsTheSliceFromTheStatement()
    {
        var total = McDonaldsYear(PlainTotal, 2_199m);
        var slice = McDonaldsYear(DepletionTotal, 457m);
        var otherLine = McDonaldsYear(Guid.NewGuid(), 9_500m);
        var (folded, _) = DepreciationTotal.Fold([total, slice], [], Concepts, FamilyPriority);

        var statement = DepreciationTotal.ForStatement(
            [slice, total, otherLine],
            2025,
            folded,
            Concepts
        );

        statement.Should().Equal(total, otherLine);
    }

    [Fact]
    public void ForStatement_NoTotalTagged_AddsTheFiscalYearsSums()
    {
        var otherLine = Fact(
            Guid.NewGuid(),
            136_162m,
            "2025-07-01",
            "2026-06-30",
            MicrosoftAnnual,
            "2026-07-29"
        );
        var (folded, _) = DepreciationTotal.Fold([], MicrosoftParts, Concepts, FamilyPriority);

        var statement = DepreciationTotal.ForStatement([otherLine], 2026, folded, Concepts);

        Millions(statement, DepreciationTotal.ConceptId).Should().Equal(39_000m);
        statement.Should().Contain(otherLine);
    }

    [Fact]
    public void ForStatement_DimensionalFactOfASlice_PassesThrough()
    {
        // A segment's figure is evidence a statement measures with; only consolidated slices go.
        var total = McDonaldsYear(PlainTotal, 2_199m);
        var slice = McDonaldsYear(DepletionTotal, 457m);
        var segment = McDonaldsYear(DepletionTotal, 120m);
        segment.DimensionsKey = "a3f1";
        var (folded, _) = DepreciationTotal.Fold([total, slice], [], Concepts, FamilyPriority);

        var statement = DepreciationTotal.ForStatement([slice, segment], 2025, folded, Concepts);

        statement.Should().ContainSingle().Which.Should().BeSameAs(segment);
    }
}
