using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderCostBasisCalculatorTests
{
    private static readonly HolderSecurityKey Stock = new(Guid.NewGuid(), null);
    private static readonly Dictionary<HolderSecurityKey, decimal?> NoFactors = [];
    private static readonly HashSet<HolderSecurityKey> Resolved = [];

    [Fact]
    public void Advance_NewPosition_StartsAtTheQuarterAveragePrice()
    {
        var slots = Advance(
            current: [new(Stock, 100, 5_000)],
            previous: [],
            previousSlots: [],
            averagePrice: 48m
        );

        slots[Stock].Should().Be(HolderCostSlot.Estimated(48m));
    }

    [Fact]
    public void Advance_Buy_AveragesTheNewSharesIn()
    {
        var slots = Advance(
            current: [new(Stock, 300, 18_000)],
            previous: [new(Stock, 100, 5_000)],
            previousSlots: new() { [Stock] = HolderCostSlot.Estimated(40m) },
            averagePrice: 55m
        );

        // 100 at 40 plus 200 at 55.
        slots[Stock].CostPerShare.Should().Be((100m * 40m + 200m * 55m) / 300m);
    }

    [Fact]
    public void Advance_Sale_KeepsTheCostPerShare()
    {
        var slots = Advance(
            current: [new(Stock, 50, 3_000)],
            previous: [new(Stock, 100, 5_000)],
            previousSlots: new() { [Stock] = HolderCostSlot.Estimated(40m) },
            averagePrice: 60m
        );

        slots[Stock].Should().Be(HolderCostSlot.Estimated(40m));
    }

    [Fact]
    public void Advance_Split_RestatesTheCarriedCostOntoTheNewBasis()
    {
        // A 4-for-1 split: the previous 100 shares already read as 400 on this quarter's basis.
        var slots = HolderCostBasisCalculator.Advance(
            [new(Stock, 400, 6_000)],
            [new(Stock, 400, 6_000)],
            new Dictionary<HolderSecurityKey, HolderCostSlot>
            {
                [Stock] = HolderCostSlot.Estimated(40m),
            },
            new Dictionary<HolderSecurityKey, decimal?> { [Stock] = 4m },
            Resolved,
            new Dictionary<HolderSecurityKey, decimal> { [Stock] = 15m }
        );

        slots[Stock].Should().Be(HolderCostSlot.Estimated(10m));
    }

    [Fact]
    public void Advance_UnresolvedSplitBasis_LeavesThePositionUnpriced()
    {
        var slots = HolderCostBasisCalculator.Advance(
            [new(Stock, 400, 6_000)],
            [new(Stock, 100, 6_000)],
            new Dictionary<HolderSecurityKey, HolderCostSlot>
            {
                [Stock] = HolderCostSlot.Estimated(40m),
            },
            NoFactors,
            new HashSet<HolderSecurityKey> { Stock },
            new Dictionary<HolderSecurityKey, decimal> { [Stock] = 15m }
        );

        slots[Stock].Should().Be(HolderCostSlot.Unpriced);
    }

    [Fact]
    public void Advance_BuyWithoutATrustedPrice_IsUnpricedAndStaysSoUntilASale()
    {
        var unpriced = Advance(
            current: [new(Stock, 300, 18_000)],
            previous: [new(Stock, 100, 5_000)],
            previousSlots: new() { [Stock] = HolderCostSlot.Estimated(40m) },
            averagePrice: null
        );
        unpriced[Stock].Should().Be(HolderCostSlot.Unpriced);

        var carried = Advance(
            current: [new(Stock, 200, 12_000)],
            previous: [new(Stock, 300, 18_000)],
            previousSlots: new() { [Stock] = HolderCostSlot.Unpriced },
            averagePrice: 60m
        );
        carried[Stock].Should().Be(HolderCostSlot.Unpriced);
    }

    [Fact]
    public void Advance_SharesFarFromTheReportedValue_AreNotCosted()
    {
        // 100 shares at a $48 average price is $4,800, far above a $10 reported position.
        var slots = Advance(
            current: [new(Stock, 100, 10)],
            previous: [],
            previousSlots: [],
            averagePrice: 48m
        );

        slots[Stock].Should().Be(HolderCostSlot.Unpriced);
    }

    [Fact]
    public void Advance_NoComparableQuarter_LeavesEveryEntryUnknown()
    {
        var slots = HolderCostBasisCalculator.Advance(
            [new(Stock, 100, 5_000)],
            null,
            new Dictionary<HolderSecurityKey, HolderCostSlot>(),
            NoFactors,
            Resolved,
            new Dictionary<HolderSecurityKey, decimal> { [Stock] = 48m }
        );

        slots[Stock].Should().Be(HolderCostSlot.UnknownEntry);
    }

    [Fact]
    public void Advance_HeldWithoutACarriedSlot_IsAnUnknownEntry()
    {
        var slots = Advance(
            current: [new(Stock, 200, 10_000)],
            previous: [new(Stock, 100, 5_000)],
            previousSlots: [],
            averagePrice: 50m
        );

        slots[Stock].Should().Be(HolderCostSlot.UnknownEntry);
    }

    [Fact]
    public void Advance_ReentryAfterAFullSale_StartsAFreshCost()
    {
        // Sold out last quarter (no previous position), bought back this quarter.
        var slots = Advance(
            current: [new(Stock, 100, 7_000)],
            previous: [new(new HolderSecurityKey(Guid.NewGuid(), null), 10, 100)],
            previousSlots: new() { [Stock] = HolderCostSlot.Estimated(20m) },
            averagePrice: 70m
        );

        slots[Stock].Should().Be(HolderCostSlot.Estimated(70m));
    }

    [Fact]
    public void Advance_SiblingClasses_AreCostedApart()
    {
        var classB = Stock with { ListedTicker = "STK.B" };
        var slots = HolderCostBasisCalculator.Advance(
            [new(Stock, 100, 5_000), new(classB, 100, 9_000)],
            [],
            new Dictionary<HolderSecurityKey, HolderCostSlot>(),
            NoFactors,
            Resolved,
            new Dictionary<HolderSecurityKey, decimal> { [Stock] = 50m, [classB] = 90m }
        );

        slots[Stock].Should().Be(HolderCostSlot.Estimated(50m));
        slots[classB].Should().Be(HolderCostSlot.Estimated(90m));
    }

    private static Dictionary<HolderSecurityKey, HolderCostSlot> Advance(
        List<HolderEquityPosition> current,
        List<HolderEquityPosition> previous,
        Dictionary<HolderSecurityKey, HolderCostSlot> previousSlots,
        decimal? averagePrice
    ) =>
        HolderCostBasisCalculator.Advance(
            current,
            previous,
            previousSlots,
            NoFactors,
            Resolved,
            averagePrice is { } price
                ? new Dictionary<HolderSecurityKey, decimal> { [Stock] = price }
                : []
        );
}
