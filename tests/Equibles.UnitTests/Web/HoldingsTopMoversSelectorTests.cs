using Equibles.Web.Services;
using Equibles.Web.ViewModels.Stocks;

namespace Equibles.UnitTests.Web;

public class HoldingsTopMoversSelectorTests
{
    [Fact]
    public void Select_EmptyDictionary_ReturnsEmptyLists()
    {
        var (buyers, sellers) = HoldingsTopMoversSelector.Select([], 5);

        buyers.Should().BeEmpty();
        sellers.Should().BeEmpty();
    }

    [Fact]
    public void Select_MaxZero_ReturnsEmptyLists()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.New] = [Mover(deltaShares: 100, currentShares: 100)],
        };

        var (buyers, sellers) = HoldingsTopMoversSelector.Select(grouped, 0);

        buyers.Should().BeEmpty();
        sellers.Should().BeEmpty();
    }

    [Fact]
    public void Select_BuyersRankedByDeltaSharesDescending()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.Increased] =
            [
                Mover(deltaShares: 50, currentShares: 150),
                Mover(deltaShares: 200, currentShares: 300),
            ],
            [PositionChangeType.New] = [Mover(deltaShares: 100, currentShares: 100)],
        };

        var (buyers, _) = HoldingsTopMoversSelector.Select(grouped, 5);

        buyers.Should().HaveCount(3);
        buyers.Select(b => b.DeltaShares).Should().ContainInOrder(200, 100, 50);
    }

    [Fact]
    public void Select_SellersRankedByDeltaSharesAscending()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.Reduced] =
            [
                Mover(deltaShares: -50, currentShares: 100, previousShares: 150),
                Mover(deltaShares: -200, currentShares: 50, previousShares: 250),
            ],
            [PositionChangeType.SoldOut] =
            [
                Mover(deltaShares: -100, currentShares: 0, previousShares: 100),
            ],
        };

        var (_, sellers) = HoldingsTopMoversSelector.Select(grouped, 5);

        sellers.Should().HaveCount(3);
        // Most-negative first.
        sellers.Select(s => s.DeltaShares).Should().ContainInOrder(-200, -100, -50);
    }

    [Fact]
    public void Select_CapsAtMax()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.New] = Enumerable
                .Range(1, 10)
                .Select(i => Mover(deltaShares: i * 100, currentShares: i * 100))
                .ToList(),
            [PositionChangeType.Reduced] = Enumerable
                .Range(1, 10)
                .Select(i =>
                    Mover(deltaShares: -i * 100, currentShares: 0, previousShares: i * 100)
                )
                .ToList(),
        };

        var (buyers, sellers) = HoldingsTopMoversSelector.Select(grouped, 5);

        buyers.Should().HaveCount(5);
        // Top buyer should be the largest Δ shares from the New list.
        buyers[0].DeltaShares.Should().Be(1000);
        sellers.Should().HaveCount(5);
        sellers[0].DeltaShares.Should().Be(-1000);
    }

    [Fact]
    public void Select_OnlySellersExist_BuyersIsEmpty()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.SoldOut] =
            [
                Mover(deltaShares: -500, currentShares: 0, previousShares: 500),
            ],
        };

        var (buyers, sellers) = HoldingsTopMoversSelector.Select(grouped, 5);

        buyers.Should().BeEmpty();
        sellers.Should().HaveCount(1);
    }

    [Fact]
    public void CountBuyers_SumsNewAndIncreased()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.New] = [Mover(deltaShares: 100, currentShares: 100)],
            [PositionChangeType.Increased] =
            [
                Mover(deltaShares: 50, currentShares: 150),
                Mover(deltaShares: 75, currentShares: 175),
            ],
            [PositionChangeType.Reduced] = [Mover(deltaShares: -10, currentShares: 90)],
        };

        HoldingsTopMoversSelector.CountBuyers(grouped).Should().Be(3);
    }

    [Fact]
    public void CountSellers_SumsReducedAndSoldOut()
    {
        var grouped = new Dictionary<PositionChangeType, List<HolderPositionChange>>
        {
            [PositionChangeType.Reduced] = [Mover(deltaShares: -10, currentShares: 90)],
            [PositionChangeType.SoldOut] =
            [
                Mover(deltaShares: -100, currentShares: 0, previousShares: 100),
                Mover(deltaShares: -50, currentShares: 0, previousShares: 50),
            ],
            [PositionChangeType.New] = [Mover(deltaShares: 100, currentShares: 100)],
        };

        HoldingsTopMoversSelector.CountSellers(grouped).Should().Be(3);
    }

    private static HolderPositionChange Mover(
        long deltaShares,
        long currentShares,
        long previousShares = 0,
        long currentValue = 0,
        long previousValue = 0
    )
    {
        // deltaShares is asserted via the Current/Previous delta — keep them consistent.
        previousShares = currentShares - deltaShares;
        return new HolderPositionChange
        {
            InstitutionalHolderId = Guid.NewGuid(),
            CurrentShares = currentShares,
            PreviousShares = previousShares,
            CurrentValue = currentValue,
            PreviousValue = previousValue,
        };
    }
}
