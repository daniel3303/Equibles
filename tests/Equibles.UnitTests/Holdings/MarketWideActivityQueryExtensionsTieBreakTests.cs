using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class MarketWideActivityQueryExtensionsTieBreakTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void EnumerableBuckets_BreakHeadlineMetricTiesByStockId()
    {
        AssertActivityOrder(ActivityRows().TopBuyers());
        AssertActivityOrder(SellerRows().TopSellers());
        AssertChurnOrder(ChurnRows().NewPositions());
        AssertChurnOrder(ChurnRows().SoldOutPositions());
    }

    [Fact]
    public void QueryableBuckets_BreakHeadlineMetricTiesByStockId()
    {
        AssertActivityOrder(ActivityRows().AsQueryable().TopBuyers());
        AssertActivityOrder(SellerRows().AsQueryable().TopSellers());
        AssertChurnOrder(ChurnRows().AsQueryable().NewPositions());
        AssertChurnOrder(ChurnRows().AsQueryable().SoldOutPositions());
    }

    private static MarketWideStockActivity[] ActivityRows() =>
        [
            new()
            {
                CommonStockId = Second,
                CurrentShares = 2,
                PreviousShares = 1,
                CurrentValue = 20,
                PreviousValue = 10,
            },
            new()
            {
                CommonStockId = First,
                CurrentShares = 2,
                PreviousShares = 1,
                CurrentValue = 20,
                PreviousValue = 10,
            },
        ];

    private static MarketWideStockActivity[] SellerRows() =>
        [
            new()
            {
                CommonStockId = Second,
                CurrentShares = 1,
                PreviousShares = 2,
                CurrentValue = 10,
                PreviousValue = 20,
            },
            new()
            {
                CommonStockId = First,
                CurrentShares = 1,
                PreviousShares = 2,
                CurrentValue = 10,
                PreviousValue = 20,
            },
        ];

    private static MarketWideStockChurn[] ChurnRows() =>
        [
            new()
            {
                CommonStockId = Second,
                NewFilerCount = 3,
                SoldOutFilerCount = 4,
            },
            new()
            {
                CommonStockId = First,
                NewFilerCount = 3,
                SoldOutFilerCount = 4,
            },
        ];

    private static void AssertActivityOrder(IEnumerable<MarketWideStockActivity> rows) =>
        rows.Select(r => r.CommonStockId).Should().Equal(First, Second);

    private static void AssertChurnOrder(IEnumerable<MarketWideStockChurn> rows) =>
        rows.Select(r => r.CommonStockId).Should().Equal(First, Second);
}
