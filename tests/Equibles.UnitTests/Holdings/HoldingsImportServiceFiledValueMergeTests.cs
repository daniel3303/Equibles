using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceFiledValueMergeTests
{
    private static readonly Guid StockId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HolderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static InstitutionalHolding Leg(long shares, long value, long? filedValue) =>
        new()
        {
            CommonStockId = StockId,
            InstitutionalHolderId = HolderId,
            ReportDate = new DateOnly(2020, 3, 31),
            ShareType = ShareType.Shares,
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = value,
            FiledValue = filedValue,
        };

    private static bool AddOrMerge(
        Dictionary<string, InstitutionalHolding> map,
        InstitutionalHolding holding
    )
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "AddOrMergeHolding",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        method.Should().NotBeNull();
        return (bool)method.Invoke(null, [map, holding, new HoldingManagerEntry()]);
    }

    [Fact]
    public void AddOrMergeHolding_PositionSplitAcrossManagerLegs_AccumulatesTheFiledValueToo()
    {
        // A filer that discloses one security across several otherManager codes files a value per
        // leg, and the merge sums Shares and Value across them. FiledValue exists to audit that
        // sum, so it has to accumulate with it: keeping only the first leg's figure leaves the row
        // claiming a fraction of what was filed, and every multi-leg position then reads as a gross
        // derived-vs-filed disagreement that is an artefact of this merge rather than a real basis
        // problem. On the first production re-import that was 147,182 positions in one quarter,
        // which is enough noise to bury the genuine signals the check exists to surface.
        var map = new Dictionary<string, InstitutionalHolding>();

        AddOrMerge(map, Leg(shares: 100, value: 1_000, filedValue: 900)).Should().BeTrue();
        AddOrMerge(map, Leg(shares: 50, value: 500, filedValue: 450)).Should().BeFalse();

        var merged = map.Values.Should().ContainSingle().Subject;
        merged.Shares.Should().Be(150);
        merged.Value.Should().Be(1_500);
        merged.FiledValue.Should().Be(1_350);
    }

    [Fact]
    public void AddOrMergeHolding_NoLegFiledAValue_LeavesItNull()
    {
        // Schedule 13D/G reports no value at all. Summing nulls into 0 would turn "the filing does
        // not carry this figure" into "the filer said zero", and the audit would then compare
        // against a number nobody filed.
        var map = new Dictionary<string, InstitutionalHolding>();

        AddOrMerge(map, Leg(shares: 100, value: 1_000, filedValue: null));
        AddOrMerge(map, Leg(shares: 50, value: 500, filedValue: null));

        map.Values.Single().FiledValue.Should().BeNull();
    }

    [Fact]
    public void AddOrMergeHolding_OnlyOneLegFiledAValue_KeepsWhatWasFiled()
    {
        // A partially-populated position must report the figure that exists rather than collapsing
        // to null, so the audit still has something to compare and the row is not silently dropped
        // from the check.
        var map = new Dictionary<string, InstitutionalHolding>();

        AddOrMerge(map, Leg(shares: 100, value: 1_000, filedValue: null));
        AddOrMerge(map, Leg(shares: 50, value: 500, filedValue: 450));

        map.Values.Single().FiledValue.Should().Be(450);
    }
}
