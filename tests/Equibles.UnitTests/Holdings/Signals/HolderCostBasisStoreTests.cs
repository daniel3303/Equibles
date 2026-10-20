using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.HostedService.Services.Signals;

namespace Equibles.UnitTests.Holdings.Signals;

public class HolderCostBasisStoreTests
{
    private static readonly Guid Holder = Guid.NewGuid();

    [Fact]
    public void PreviousSlots_ReadAStoredCostAsEstimatedAndABitAsUnpriced()
    {
        var costed = new HolderSecurityKey(Guid.NewGuid(), null);
        var unpriced = new HolderSecurityKey(Guid.NewGuid(), null);
        var unknown = new HolderSecurityKey(Guid.NewGuid(), null);

        var slots = HolderCostBasisStore.PreviousSlots([
            Row(costed, previousCost: 12.5f),
            Row(unpriced, previousUnpriced: true),
            Row(unknown),
        ]);

        slots[costed].Should().Be(HolderCostSlot.Estimated(12.5m));
        slots[unpriced].Should().Be(HolderCostSlot.Unpriced);
        slots.Should().NotContainKey(unknown);
    }

    [Fact]
    public void Diff_WritesOnlyChangedSlotsAndClearsPositionsNoLongerHeld()
    {
        var unchanged = new HolderSecurityKey(Guid.NewGuid(), null);
        var changed = new HolderSecurityKey(Guid.NewGuid(), null);
        var sold = new HolderSecurityKey(Guid.NewGuid(), null);
        var opened = new HolderSecurityKey(Guid.NewGuid(), null);
        var unknownEntry = new HolderSecurityKey(Guid.NewGuid(), null);
        var stored = new List<HolderCostBasisStore.CostRow>
        {
            Row(unchanged, cost: 10f),
            Row(changed, cost: 10f),
            Row(sold, cost: 7f),
        };

        var writes = HolderCostBasisStore.Diff(
            new Dictionary<Guid, IReadOnlyDictionary<HolderSecurityKey, HolderCostSlot>>
            {
                [Holder] = new Dictionary<HolderSecurityKey, HolderCostSlot>
                {
                    [unchanged] = HolderCostSlot.Estimated(10m),
                    [changed] = HolderCostSlot.Unpriced,
                    [opened] = HolderCostSlot.Estimated(42m),
                    [unknownEntry] = HolderCostSlot.UnknownEntry,
                },
            },
            stored.ToLookup(row => row.InstitutionalHolderId)
        );

        writes
            .Should()
            .BeEquivalentTo([
                new HolderCostBasisStore.CostWrite(Holder, changed, stored[1].Id, 0f, true),
                new HolderCostBasisStore.CostWrite(Holder, opened, null, 42f, false),
                new HolderCostBasisStore.CostWrite(Holder, sold, stored[2].Id, 0f, false),
            ]);
    }

    private static HolderCostBasisStore.CostRow Row(
        HolderSecurityKey security,
        float previousCost = 0,
        bool previousUnpriced = false,
        float cost = 0
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            InstitutionalHolderId = Holder,
            Security = security,
            PreviousCost = previousCost,
            PreviousUnpriced = previousUnpriced,
            Cost = cost,
        };
}
