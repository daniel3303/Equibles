using Equibles.Holdings.Data.Models;
using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class HoldingsPositionGrouperTests
{
    [Fact]
    public void Group_BothInputsEmpty_ReturnsEmptyBuckets()
    {
        var result = HoldingsPositionGrouper.Group([], []);

        result.Should().ContainKey(PositionChangeType.New).WhoseValue.Should().BeEmpty();
        result.Should().ContainKey(PositionChangeType.Increased).WhoseValue.Should().BeEmpty();
        result.Should().ContainKey(PositionChangeType.Reduced).WhoseValue.Should().BeEmpty();
        result.Should().ContainKey(PositionChangeType.Unchanged).WhoseValue.Should().BeEmpty();
        result.Should().ContainKey(PositionChangeType.SoldOut).WhoseValue.Should().BeEmpty();
    }

    [Fact]
    public void Group_CurrentOnly_ClassifiesAsNew()
    {
        var holderId = Guid.NewGuid();
        var current = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([current], []);

        result[PositionChangeType.New].Should().HaveCount(1);
        result[PositionChangeType.New][0].InstitutionalHolderId.Should().Be(holderId);
        result[PositionChangeType.New][0].CurrentShares.Should().Be(100);
        result[PositionChangeType.New][0].PreviousShares.Should().Be(0);
        result[PositionChangeType.New][0].DeltaShares.Should().Be(100);
        result[PositionChangeType.New][0].DeltaValue.Should().Be(1000);
    }

    [Fact]
    public void Group_PreviousOnly_ClassifiesAsSoldOut()
    {
        var holderId = Guid.NewGuid();
        var previous = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([], [previous]);

        result[PositionChangeType.SoldOut].Should().HaveCount(1);
        var entry = result[PositionChangeType.SoldOut][0];
        entry.InstitutionalHolderId.Should().Be(holderId);
        entry.CurrentHolding.Should().BeNull();
        entry.CurrentShares.Should().Be(0);
        entry.PreviousShares.Should().Be(100);
        entry.DeltaShares.Should().Be(-100);
        entry.DeltaValue.Should().Be(-1000);
    }

    [Fact]
    public void Group_HigherShareCount_ClassifiesAsIncreased()
    {
        var holderId = Guid.NewGuid();
        var current = Holding(holderId, shares: 150, value: 1500);
        var previous = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([current], [previous]);

        result[PositionChangeType.Increased].Should().HaveCount(1);
        result[PositionChangeType.Increased][0].DeltaShares.Should().Be(50);
    }

    [Fact]
    public void Group_LowerShareCount_ClassifiesAsReduced()
    {
        var holderId = Guid.NewGuid();
        var current = Holding(holderId, shares: 60, value: 600);
        var previous = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([current], [previous]);

        result[PositionChangeType.Reduced].Should().HaveCount(1);
        result[PositionChangeType.Reduced][0].DeltaShares.Should().Be(-40);
    }

    [Fact]
    public void Group_EqualShareCount_ClassifiesAsUnchanged()
    {
        var holderId = Guid.NewGuid();
        var current = Holding(holderId, shares: 100, value: 1100);
        var previous = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([current], [previous]);

        result[PositionChangeType.Unchanged].Should().HaveCount(1);
        result[PositionChangeType.Unchanged][0].DeltaShares.Should().Be(0);
        result[PositionChangeType.Unchanged][0].DeltaValue.Should().Be(100);
    }

    [Fact]
    public void Group_MultipleFilingsPerHolder_AggregatesShares()
    {
        var holderId = Guid.NewGuid();
        var current1 = Holding(holderId, shares: 30, value: 300);
        var current2 = Holding(holderId, shares: 70, value: 700);
        var previous = Holding(holderId, shares: 100, value: 1000);

        var result = HoldingsPositionGrouper.Group([current1, current2], [previous]);

        // Two filings sum to 100 shares — same as previous → Unchanged.
        result[PositionChangeType.Unchanged].Should().HaveCount(1);
        result[PositionChangeType.Unchanged][0].CurrentShares.Should().Be(100);
    }

    [Fact]
    public void Group_AllBucketsAtOnce_SplitsCorrectly()
    {
        var newId = Guid.NewGuid();
        var increasedId = Guid.NewGuid();
        var reducedId = Guid.NewGuid();
        var unchangedId = Guid.NewGuid();
        var soldOutId = Guid.NewGuid();

        var current = new[]
        {
            Holding(newId, 50, 500),
            Holding(increasedId, 200, 2000),
            Holding(reducedId, 50, 500),
            Holding(unchangedId, 100, 1000),
        };
        var previous = new[]
        {
            Holding(increasedId, 100, 1000),
            Holding(reducedId, 100, 1000),
            Holding(unchangedId, 100, 1000),
            Holding(soldOutId, 100, 1000),
        };

        var result = HoldingsPositionGrouper.Group(current, previous);

        result[PositionChangeType.New]
            .Should()
            .ContainSingle()
            .Which.InstitutionalHolderId.Should()
            .Be(newId);
        result[PositionChangeType.Increased]
            .Should()
            .ContainSingle()
            .Which.InstitutionalHolderId.Should()
            .Be(increasedId);
        result[PositionChangeType.Reduced]
            .Should()
            .ContainSingle()
            .Which.InstitutionalHolderId.Should()
            .Be(reducedId);
        result[PositionChangeType.Unchanged]
            .Should()
            .ContainSingle()
            .Which.InstitutionalHolderId.Should()
            .Be(unchangedId);
        result[PositionChangeType.SoldOut]
            .Should()
            .ContainSingle()
            .Which.InstitutionalHolderId.Should()
            .Be(soldOutId);
    }

    private static InstitutionalHolding Holding(Guid holderId, long shares, long value)
    {
        return new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            InstitutionalHolder = new InstitutionalHolder { Id = holderId, Name = "Test Holder" },
            Shares = shares,
            Value = value,
            FilingDate = new DateOnly(2025, 1, 15),
            ReportDate = new DateOnly(2024, 12, 31),
        };
    }
}
