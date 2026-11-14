using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;

namespace Equibles.UnitTests.Sec;

public class SharesOutstandingAsOfReaderPickTests
{
    private static readonly Guid Issuer = Guid.NewGuid();

    [Fact]
    public void Pick_LatestConsolidatedCount_WinsOverAnOlderOne()
    {
        var picked = SharesOutstandingAsOfReader.Pick(
            [
                Fact(1_000, new DateOnly(2025, 10, 20), "a1"),
                Fact(1_100, new DateOnly(2026, 4, 20), "a2"),
            ],
            new Dictionary<Guid, (string, string)>()
        );

        picked
            .Should()
            .Be(new SharesOutstandingAsOf(1_100, new DateOnly(2026, 4, 20), false, false));
    }

    [Fact]
    public void Pick_ClassCountsLaterThanTheConsolidatedOne_SumEachClassOnce()
    {
        var classA = Fact(600, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var classB = Fact(400, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var classBAgain = Fact(400, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var picked = SharesOutstandingAsOfReader.Pick(
            [Fact(900, new DateOnly(2026, 1, 20), "a1"), classA, classB, classBAgain],
            new Dictionary<Guid, (string, string)>
            {
                [classA.Id] = ("us-gaap:StatementClassOfStockAxis", "ClassA"),
                [classB.Id] = ("us-gaap:StatementClassOfStockAxis", "ClassB"),
                [classBAgain.Id] = ("us-gaap:StatementClassOfStockAxis", "ClassB"),
            }
        );

        picked
            .Should()
            .Be(new SharesOutstandingAsOf(1_000, new DateOnly(2026, 4, 20), true, false));
    }

    // The filer's main class is tagged as the common-stock member beside its Class B: dropped as a
    // roll-up, the count was Class B alone and every holder's share of the company a multiple of it.
    [Fact]
    public void Pick_CommonStockMemberBesideAnotherClass_CountsBoth()
    {
        var common = Fact(35_000_000, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var classB = Fact(5_600_000, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var picked = SharesOutstandingAsOfReader.Pick(
            [common, classB],
            new Dictionary<Guid, (string, string)>
            {
                [common.Id] = ("us-gaap:StatementClassOfStockAxis", "us-gaap:CommonStockMember"),
                [classB.Id] = ("us-gaap:StatementClassOfStockAxis", "us-gaap:CommonClassBMember"),
            }
        );

        picked
            .Should()
            .Be(new SharesOutstandingAsOf(40_600_000, new DateOnly(2026, 4, 20), true, false));
    }

    [Fact]
    public void Pick_ConsolidatedCountForTheSameDate_IsTheEntityFigure()
    {
        var classB = Fact(5_600_000, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var picked = SharesOutstandingAsOfReader.Pick(
            [Fact(40_600_000, new DateOnly(2026, 4, 20), "a2"), classB],
            new Dictionary<Guid, (string, string)>
            {
                [classB.Id] = ("us-gaap:StatementClassOfStockAxis", "us-gaap:CommonClassBMember"),
            }
        );

        picked
            .Should()
            .Be(new SharesOutstandingAsOf(40_600_000, new DateOnly(2026, 4, 20), false, false));
    }

    [Fact]
    public void Pick_ClassDatedOnlyAtAnotherDate_FallsBackToTheConsolidatedCount()
    {
        var classA = Fact(600, new DateOnly(2026, 4, 20), "a2", consolidated: false);
        var classB = Fact(400, new DateOnly(2026, 3, 31), "a2", consolidated: false);
        var picked = SharesOutstandingAsOfReader.Pick(
            [Fact(900, new DateOnly(2026, 1, 20), "a1"), classA, classB],
            new Dictionary<Guid, (string, string)>
            {
                [classA.Id] = ("us-gaap:StatementClassOfStockAxis", "ClassA"),
                [classB.Id] = ("us-gaap:StatementClassOfStockAxis", "ClassB"),
            }
        );

        picked.Should().Be(new SharesOutstandingAsOf(900, new DateOnly(2026, 1, 20), false, false));
    }

    [Fact]
    public void Pick_ForeignAnnualReport_IsMarkedForeign()
    {
        var picked = SharesOutstandingAsOfReader.Pick(
            [Fact(5_000, new DateOnly(2025, 12, 31), "f1", form: DocumentType.TwentyF)],
            new Dictionary<Guid, (string, string)>()
        );

        picked.IsForeignForm.Should().BeTrue();
    }

    [Fact]
    public void Pick_NoPositiveCount_ReturnsNull() =>
        SharesOutstandingAsOfReader
            .Pick(
                [Fact(0, new DateOnly(2026, 1, 20), "a1")],
                new Dictionary<Guid, (string, string)>()
            )
            .Should()
            .BeNull();

    private static SharesOutstandingAsOfReader.FactRow Fact(
        decimal value,
        DateOnly periodEnd,
        string accession,
        bool consolidated = true,
        DocumentType form = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            IssuerId = Issuer,
            Value = value,
            PeriodEnd = periodEnd,
            FiledDate = periodEnd.AddDays(5),
            Form = form ?? DocumentType.TenQ,
            AccessionNumber = accession,
            Consolidated = consolidated,
        };
}
