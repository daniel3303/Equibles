using Equibles.Sec.FinancialFacts.BusinessLogic;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The member combinations filers state on the cover page's class-of-stock axis. Three readers
/// summed them by three rules, so one cover page gave three share counts.
/// </summary>
public class ShareClassTotalTests
{
    private const string Common = "us-gaap:CommonStockMember";
    private const string ClassA = "us-gaap:CommonClassAMember";
    private const string ClassB = "us-gaap:CommonClassBMember";

    [Fact]
    public void Of_TwoClasses_IsTheirSum() =>
        ShareClassTotal.Of([(ClassA, 600m), (ClassB, 400m)]).Should().Be(1_000m);

    [Fact]
    public void Of_CommonStockMemberBesideAnotherClass_IsTheMainClassAndCounts() =>
        ShareClassTotal.Of([(Common, 35_000_000m), (ClassB, 5_600_000m)]).Should().Be(40_600_000m);

    [Fact]
    public void Of_CommonStockMemberEqualToTheOtherClasses_IsTheirRollUpAndCountsOnce() =>
        ShareClassTotal.Of([(ClassA, 600m), (ClassB, 400m), (Common, 1_000m)]).Should().Be(1_000m);

    [Fact]
    public void Of_CommonStockMemberAlone_IsTheCount() =>
        ShareClassTotal.Of([(Common, 1_000m)]).Should().Be(1_000m);

    [Theory]
    [InlineData("us-gaap:TreasuryStockMember")]
    [InlineData("us-gaap:TreasuryStockCommonMember")]
    [InlineData("dei:AdrMember")]
    public void Of_TreasuryOrReceiptMember_NeverCounts(string member)
    {
        ShareClassTotal.Of([(ClassA, 600m), (member, 50m)]).Should().Be(600m);
        ShareClassTotal.Of([(member, 50m)]).Should().BeNull();
    }

    [Fact]
    public void Of_ClassStatedTwice_CountsOnce() =>
        ShareClassTotal.Of([(ClassA, 600m), (ClassB, 400m), (ClassB, 400m)]).Should().Be(1_000m);

    [Fact]
    public void StatesEveryClassAt_ClassDatedElsewhere_IsFalse()
    {
        DateOnly asOf = new(2026, 4, 20),
            earlier = new(2026, 3, 31);

        ShareClassTotal
            .StatesEveryClassAt([(ClassA, asOf), (ClassB, earlier)], asOf)
            .Should()
            .BeFalse();
        ShareClassTotal
            .StatesEveryClassAt([(ClassA, asOf), (ClassB, asOf), (ClassB, earlier)], asOf)
            .Should()
            .BeTrue();
    }
}
