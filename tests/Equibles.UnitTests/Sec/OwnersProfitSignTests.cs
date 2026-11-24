using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Owners' profit tagged with the opposite sign. Figures are PicPay's (BRL, each profit tagged as a
/// loss) and Solmate's (EUR, each loss tagged as a profit): total profit and the minority share
/// carry the printed signs, and total = owners + minority holds only once the owners' figure is
/// negated.
/// </summary>
public class OwnersProfitSignTests
{
    [Theory]
    [InlineData(
        "0001213900-26-049950",
        "2025-01-01",
        "2025-12-31",
        "BRL",
        1_141_606_000,
        -1_091_487_000,
        50_119_000,
        1_091_487_000
    )]
    [InlineData(
        "0001213900-26-093207",
        "2026-01-01",
        "2026-06-30",
        "BRL",
        420_765_000,
        -421_616_000,
        -851_000,
        421_616_000
    )]
    [InlineData(
        "0001213900-26-093207",
        "2026-04-01",
        "2026-06-30",
        "BRL",
        269_028_000,
        -270_186_000,
        -1_158_000,
        270_186_000
    )]
    [InlineData(
        "0001213900-25-044434",
        "2024-01-01",
        "2024-12-31",
        "EUR",
        -5_048_861,
        4_427_545,
        -621_316,
        -4_427_545
    )]
    public void Read_OwnersProfitTaggedWithTheOppositeSign_IsReadWithTheSignItsFilingProves(
        string accession,
        string start,
        string end,
        string unit,
        long total,
        long owners,
        long minority,
        long expected
    )
    {
        var (evidence, concepts) = Facts(
            accession,
            DateOnly.Parse(start),
            DateOnly.Parse(end),
            new(2026, 9, 1),
            [
                (Ifrs, "ProfitLoss", unit, total),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", unit, minority),
            ]
        );
        // A 6-K's half year and quarter prove it like a year: the line never carries two signs.
        evidence.ForEach(fact => fact.Form = DocumentType.SixK);
        var tagged = Owners(
            accession,
            DateOnly.Parse(start),
            DateOnly.Parse(end),
            new(2026, 9, 1),
            unit,
            owners
        );

        var reading = OwnersProfitSign.Read([tagged], OwnersIds, evidence, concepts);

        var read = reading.Facts.Should().ContainSingle().Subject;
        read.Value.Should().Be(expected);
        (read.FinancialConceptId, read.PeriodStart, read.PeriodEnd, read.AccessionNumber)
            .Should()
            .Be((tagged.FinancialConceptId, tagged.PeriodStart, tagged.PeriodEnd, accession));
        reading.Negated.Should().ContainSingle().Which.Should().BeSameAs(read);
        read.Id.Should().Be(tagged.Id);
        tagged.Value.Should().Be(owners, "the stored fact is never rewritten");
    }

    [Fact]
    public void Read_ReportsThatTagOneYearWithBothSigns_ReadOneSign()
    {
        // Solmate's 2024 report tags its 2023 loss as a loss; its 2025 report tags the same year as a profit.
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        DateOnly start = new(2023, 1, 1),
            end = new(2023, 12, 31);
        (FactTaxonomy, string, string, decimal)[] rows =
        [
            (Ifrs, "ProfitLoss", "EUR", -4_911_665m),
            (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "EUR", -473_225m),
        ];
        List<FinancialFact> evidence =
        [
            .. Facts(concepts, "0001213900-24-062719", start, end, new(2024, 7, 19), rows),
            .. Facts(concepts, "0001213900-25-044434", start, end, new(2025, 5, 15), rows),
        ];

        var reading = OwnersProfitSign.Read(
            [
                Owners("0001213900-24-062719", start, end, new(2024, 7, 19), "EUR", -4_438_440m),
                Owners("0001213900-25-044434", start, end, new(2025, 5, 15), "EUR", 4_438_440m),
            ],
            OwnersIds,
            evidence,
            ById(concepts)
        );

        reading.Facts.Select(fact => fact.Value).Should().Equal(-4_438_440m, -4_438_440m);
    }

    // BBVA's 2025 report tags its minority share negative: total 11,126 is owners' 10,511 plus 615,
    // so the owners' figure stands (EUR millions). A minority share inside the figures' rounding
    // cannot tell a mis-signed total from a mis-signed owners' figure; a total inside it says
    // nothing of any sign. An identity that already holds, one no single sign repairs, and an
    // owners' figure inside the rounding (the identity holds with either sign) prove nothing.
    [Theory]
    [InlineData(11_126_000_000, 10_511_000_000, -615_000_000)]
    [InlineData(151_737_000, -151_737_000, 0)]
    [InlineData(151_737_000, -151_736_000, 1_000)]
    [InlineData(1_000, 90_001_000, 90_002_000)]
    [InlineData(100_000_000, 90_000_000, 10_000_000)]
    [InlineData(100_000_000, -70_000_000, 10_000_000)]
    [InlineData(10_000_000, 1_000, 10_000_000)]
    public void Read_FilingThatDoesNotProveTheOppositeSign_KeepsTheTaggedFigure(
        long total,
        long owners,
        long minority
    )
    {
        var (evidence, concepts) = Facts(
            "0001628280-26-010001",
            YearStart,
            YearEnd,
            new(2026, 2, 20),
            [
                (Ifrs, "ProfitLoss", "EUR", total),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "EUR", minority),
            ]
        );
        var tagged = Owners(
            "0001628280-26-010001",
            YearStart,
            YearEnd,
            new(2026, 2, 20),
            "EUR",
            owners
        );

        var reading = OwnersProfitSign.Read([tagged], OwnersIds, evidence, concepts);

        reading.Facts.Should().ContainSingle().Which.Should().BeSameAs(tagged);
        reading.Negated.Should().BeEmpty();
    }

    [Fact]
    public void Read_BasicEpsOfTheTaggedSign_RefusesTheOppositeSign()
    {
        // The identity alone would negate a loss the filing also states per share.
        var (evidence, concepts) = Facts(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            [
                (Ifrs, "ProfitLoss", "BRL", 1_141_606_000m),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 50_119_000m),
                (Ifrs, "BasicEarningsLossPerShare", "BRL/shares", -3.34m),
            ]
        );
        var tagged = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );

        var reading = OwnersProfitSign.Read([tagged], OwnersIds, evidence, concepts);

        reading.Facts.Should().ContainSingle().Which.Value.Should().Be(-1_091_487_000m);
    }

    [Fact]
    public void Read_TagProvenMisSigned_KeepsOnlyFiguresTheirFilingsProve()
    {
        // PicPay's 6-K for June 2026 proves the half year mis-signed; its 2025 comparative, and the
        // 6-K that first stated that half year, are set to a minority share of nothing (one created
        // later), which checks no sign. Read as tagged, either would subtract a negative half year
        // from the twelve months; both are left out.
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        const string accession = "0001213900-26-093207";
        DateOnly filed = new(2026, 8, 25);
        DateOnly start2026 = new(2026, 1, 1),
            half2026 = new(2026, 6, 30),
            start2025 = new(2025, 1, 1),
            half2025 = new(2025, 6, 30);
        List<FinancialFact> evidence =
        [
            .. Facts(
                concepts,
                accession,
                start2026,
                half2026,
                filed,
                [
                    (Ifrs, "ProfitLoss", "BRL", 420_765_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", -851_000m),
                ]
            ),
            .. Facts(
                concepts,
                accession,
                start2025,
                half2025,
                filed,
                [
                    (Ifrs, "ProfitLoss", "BRL", 179_062_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 0m),
                ]
            ),
            .. Facts(
                concepts,
                "0001213900-25-000001",
                start2025,
                half2025,
                new(2025, 8, 20),
                [
                    (Ifrs, "ProfitLoss", "BRL", 179_062_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 0m),
                ]
            ),
        ];
        var halfYear = Owners(accession, start2026, half2026, filed, "BRL", -421_616_000m);
        var comparative = Owners(accession, start2025, half2025, filed, "BRL", -179_062_000m);
        var otherFiling = Owners(
            "0001213900-25-000001",
            start2025,
            half2025,
            new(2025, 8, 20),
            "BRL",
            -179_062_000m
        );
        var otherUnit = Owners(accession, start2025, half2025, filed, "USD", -33_000_000m);
        // The line's total profit leaves the period with it: another measure would stand in its place.
        var totalInTheWithheldPeriod = evidence.First(fact =>
            fact.PeriodEnd == half2025 && fact.Value == 179_062_000m
        );

        var reading = OwnersProfitSign.Read(
            [halfYear, comparative, otherFiling, otherUnit, totalInTheWithheldPeriod],
            OwnersIds,
            evidence,
            ById(concepts)
        );

        // A unit no filing proves mis-signed keeps its figures as tagged.
        reading
            .Facts.Select(fact => (fact.AccessionNumber, fact.PeriodEnd, fact.Unit, fact.Value))
            .Should()
            .Equal(
                (accession, half2026, "BRL", 421_616_000m),
                (accession, half2025, "USD", -33_000_000m)
            );
        reading.WithheldPeriods.Should().Equal((start2025, half2025, "BRL"));
    }

    [Fact]
    public void Read_PeriodItsFilingProvesAsTagged_KeepsItsSignBesideAFlippedOne()
    {
        const string accession = "0001213900-26-049950";
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        List<FinancialFact> evidence =
        [
            .. Facts(
                concepts,
                accession,
                YearStart,
                YearEnd,
                new(2026, 4, 30),
                [
                    (Ifrs, "ProfitLoss", "BRL", 1_141_606_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 50_119_000m),
                ]
            ),
            .. Facts(
                concepts,
                accession,
                new(2024, 1, 1),
                new(2024, 12, 31),
                new(2026, 4, 30),
                [
                    (Ifrs, "ProfitLoss", "BRL", 251_788_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 34_214_000m),
                ]
            ),
        ];
        var flipped = Owners(
            accession,
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );
        var asTagged = Owners(
            accession,
            new(2024, 1, 1),
            new(2024, 12, 31),
            new(2026, 4, 30),
            "BRL",
            217_574_000m
        );

        var reading = OwnersProfitSign.Read(
            [flipped, asTagged],
            OwnersIds,
            evidence,
            ById(concepts)
        );

        reading.Facts.Select(fact => fact.Value).Should().Equal(1_091_487_000m, 217_574_000m);
        reading.Facts[1].Should().BeSameAs(asTagged);
        reading.WithheldPeriods.Should().BeEmpty();
    }

    [Fact]
    public void Read_MinorityShareSplitByHolder_ProvesNoSign()
    {
        // The plain minority tag is not the whole share once a redeemable part is tagged beside it.
        var (evidence, concepts) = Facts(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            [
                (Gaap, "ProfitLoss", "BRL", 1_141_606_000m),
                (Gaap, "NetIncomeLossAttributableToNoncontrollingInterest", "BRL", 50_119_000m),
                (
                    Gaap,
                    "NetIncomeLossAttributableToRedeemableNoncontrollingInterest",
                    "BRL",
                    12_000_000m
                ),
            ]
        );
        var tagged = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );

        var reading = OwnersProfitSign.Read([tagged], OwnersIds, evidence, concepts);

        reading.Facts.Should().ContainSingle().Which.Should().BeSameAs(tagged);
    }

    [Fact]
    public void Read_TwoOwnersFiguresInOneFilingAndPeriod_ProveNoSign()
    {
        var (evidence, concepts) = Facts(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            [
                (Ifrs, "ProfitLoss", "BRL", 1_141_606_000m),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 50_119_000m),
            ]
        );
        var first = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );
        var second = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            1_080_000_000m
        );

        var reading = OwnersProfitSign.Read([first, second], OwnersIds, evidence, concepts);

        reading.Facts.Should().Equal(first, second);
    }

    [Fact]
    public void Read_AnotherFilingsOrAnotherPeriodsFigures_ProveNoSign()
    {
        // The half year's total and minority share, beside an owners' figure tagged for the second
        // quarter and one tagged by another filing.
        var (evidence, concepts) = Facts(
            "0001213900-26-093207",
            new(2026, 1, 1),
            new(2026, 6, 30),
            new(2026, 8, 20),
            [
                (Ifrs, "ProfitLoss", "BRL", 420_765_000m),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", -851_000m),
            ]
        );
        var quarter = Owners(
            "0001213900-26-093207",
            new(2026, 4, 1),
            new(2026, 6, 30),
            new(2026, 8, 20),
            "BRL",
            -421_616_000m
        );
        var otherFiling = Owners(
            "0001213900-26-120975",
            new(2026, 1, 1),
            new(2026, 6, 30),
            new(2026, 11, 12),
            "BRL",
            -421_616_000m
        );
        var otherUnit = Owners(
            "0001213900-26-093207",
            new(2026, 1, 1),
            new(2026, 6, 30),
            new(2026, 8, 20),
            "USD",
            -421_616_000m
        );

        var reading = OwnersProfitSign.Read(
            [quarter, otherFiling, otherUnit],
            OwnersIds,
            evidence,
            concepts
        );

        reading.Facts.Should().Equal(quarter, otherFiling, otherUnit);
    }

    [Fact]
    public void Read_FilerOfNoForeignIssuerReport_IsNotJudged()
    {
        // The rule was measured on foreign filers: a 10-K filer's figures stay as tagged.
        var (evidence, concepts) = Facts(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            [
                (Gaap, "ProfitLoss", "USD", 1_141_606_000m),
                (Gaap, "NetIncomeLossAttributableToNoncontrollingInterest", "USD", 50_119_000m),
            ]
        );
        evidence.ForEach(fact => fact.Form = DocumentType.TenK);
        var tagged = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "USD",
            -1_091_487_000m
        );
        tagged.Form = DocumentType.TenK;

        var reading = OwnersProfitSign.Read([tagged], OwnersIds, evidence, concepts);

        reading.Facts.Should().ContainSingle().Which.Should().BeSameAs(tagged);
    }

    [Theory]
    [InlineData(FactTaxonomy.UsGaap, "NetIncomeLoss", true)]
    [InlineData(FactTaxonomy.IfrsFull, "ProfitLossAttributableToOwnersOfParent", true)]
    [InlineData(FactTaxonomy.UsGaap, "ProfitLoss", false)]
    [InlineData(FactTaxonomy.IfrsFull, "ProfitLoss", false)]
    public void IsOwnersProfit_NamesTheParentAttributableTagsOnly(
        FactTaxonomy taxonomy,
        string tag,
        bool expected
    )
    {
        OwnersProfitSign.IsOwnersProfit(taxonomy, tag).Should().Be(expected);
    }

    [Fact]
    public void Read_OnePeriodOfAHistoryProvenMisSigned_IsWithheld()
    {
        // A statement for the 2025 half year alone: the June 2026 6-K in the history proves the
        // tag mis-signed, and nothing checks the half year, so it is left out.
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        const string accession = "0001213900-26-093207";
        DateOnly filed = new(2026, 8, 25);
        DateOnly start2026 = new(2026, 1, 1),
            half2026 = new(2026, 6, 30),
            start2025 = new(2025, 1, 1),
            half2025 = new(2025, 6, 30);
        List<FinancialFact> evidence =
        [
            .. Facts(
                concepts,
                accession,
                start2026,
                half2026,
                filed,
                [
                    (Ifrs, "ProfitLoss", "BRL", 420_765_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", -851_000m),
                ]
            ),
        ];
        var halfYear = Owners(accession, start2026, half2026, filed, "BRL", -421_616_000m);
        var comparative = Owners(accession, start2025, half2025, filed, "BRL", -179_062_000m);

        var alone = OwnersProfitSign.Read([comparative], OwnersIds, evidence, ById(concepts));
        var reading = OwnersProfitSign.Read(
            [comparative],
            OwnersIds,
            evidence,
            ById(concepts),
            [halfYear, comparative]
        );

        alone.Facts.Should().Equal(comparative);
        reading.Facts.Should().BeEmpty();
        reading.WithheldPeriods.Should().Equal((start2025, half2025, "BRL"));
    }

    [Fact]
    public void Read_PeriodAnotherFilingProves_KeepsTheProvenFigureAndItsTotal()
    {
        // The 2024 report states its year without a minority share; the 2025 report's comparative
        // proves the same year as tagged, so only the unchecked figure leaves.
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        const string annual2025 = "0001213900-26-049950";
        const string annual2024 = "0001213900-25-030001";
        DateOnly start2024 = new(2024, 1, 1),
            end2024 = new(2024, 12, 31);
        List<FinancialFact> evidence =
        [
            .. Facts(
                concepts,
                annual2025,
                YearStart,
                YearEnd,
                new(2026, 4, 30),
                [
                    (Ifrs, "ProfitLoss", "BRL", 1_141_606_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 50_119_000m),
                ]
            ),
            .. Facts(
                concepts,
                annual2025,
                start2024,
                end2024,
                new(2026, 4, 30),
                [
                    (Ifrs, "ProfitLoss", "BRL", 251_788_000m),
                    (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 34_214_000m),
                ]
            ),
            .. Facts(
                concepts,
                annual2024,
                start2024,
                end2024,
                new(2025, 4, 30),
                [(Ifrs, "ProfitLoss", "BRL", 251_788_000m)]
            ),
        ];
        var flipped = Owners(
            annual2025,
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );
        var proven = Owners(annual2025, start2024, end2024, new(2026, 4, 30), "BRL", 217_574_000m);
        var unchecked2024 = Owners(
            annual2024,
            start2024,
            end2024,
            new(2025, 4, 30),
            "BRL",
            217_574_000m
        );
        var total2024 = evidence[^1];

        var reading = OwnersProfitSign.Read(
            [flipped, proven, unchecked2024, total2024],
            OwnersIds,
            evidence,
            ById(concepts)
        );

        reading.Facts.Should().HaveCount(3);
        reading.Facts[1].Should().BeSameAs(proven);
        reading.Facts[2].Should().BeSameAs(total2024);
        reading.WithheldPeriods.Should().BeEmpty();
    }

    [Fact]
    public void Read_FactsOfOtherConceptsInACheckedPeriod_StayAsTheyAre()
    {
        var (evidence, concepts) = Facts(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            [
                (Ifrs, "ProfitLoss", "BRL", 1_141_606_000m),
                (Ifrs, "ProfitLossAttributableToNoncontrollingInterests", "BRL", 50_119_000m),
            ]
        );
        var tagged = Owners(
            "0001213900-26-049950",
            YearStart,
            YearEnd,
            new(2026, 4, 30),
            "BRL",
            -1_091_487_000m
        );
        var total = evidence[0];

        var reading = OwnersProfitSign.Read([tagged, total], OwnersIds, evidence, concepts);

        reading.Facts.Select(fact => fact.Value).Should().Equal(1_091_487_000m, 1_141_606_000m);
        reading.Facts[1].Should().BeSameAs(total);
    }

    private static readonly DateOnly YearStart = new(2025, 1, 1);
    private static readonly DateOnly YearEnd = new(2025, 12, 31);
    private static readonly Guid OwnersConceptId = Guid.NewGuid();
    private static readonly HashSet<Guid> OwnersIds = [OwnersConceptId];

    private static (
        List<FinancialFact> Facts,
        Dictionary<Guid, (FactTaxonomy Taxonomy, string Tag)> Concepts
    ) Facts(
        string accession,
        DateOnly start,
        DateOnly end,
        DateOnly filed,
        (FactTaxonomy Taxonomy, string Tag, string Unit, decimal Value)[] rows
    )
    {
        var concepts = new Dictionary<(FactTaxonomy, string), Guid>();
        return (Facts(concepts, accession, start, end, filed, rows), ById(concepts));
    }

    private static Dictionary<Guid, (FactTaxonomy Taxonomy, string Tag)> ById(
        Dictionary<(FactTaxonomy, string), Guid> concepts
    ) => concepts.ToDictionary(pair => pair.Value, pair => pair.Key);

    // One report's rows; reports of one company share `concepts`.
    private static List<FinancialFact> Facts(
        Dictionary<(FactTaxonomy, string), Guid> concepts,
        string accession,
        DateOnly start,
        DateOnly end,
        DateOnly filed,
        (FactTaxonomy Taxonomy, string Tag, string Unit, decimal Value)[] rows
    )
    {
        foreach (var row in rows)
            concepts.TryAdd((row.Taxonomy, row.Tag), Guid.NewGuid());
        return rows.Select(row => new FinancialFact
            {
                FinancialConceptId = concepts[(row.Taxonomy, row.Tag)],
                Unit = row.Unit,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = start,
                PeriodEnd = end,
                Value = row.Value,
                FiscalYear = end.Year,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.TwentyF,
                FiledDate = filed,
                AccessionNumber = accession,
                DimensionsKey = "",
            })
            .ToList();
    }

    // A tagged owners' profit.
    private static FinancialFact Owners(
        string accession,
        DateOnly start,
        DateOnly end,
        DateOnly filed,
        string unit,
        decimal value
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            FinancialConceptId = OwnersConceptId,
            Unit = unit,
            PeriodType = FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = value,
            FiscalYear = end.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TwentyF,
            FiledDate = filed,
            AccessionNumber = accession,
            DimensionsKey = "",
        };

    private const FactTaxonomy Gaap = FactTaxonomy.UsGaap;
    private const FactTaxonomy Ifrs = FactTaxonomy.IfrsFull;
}
