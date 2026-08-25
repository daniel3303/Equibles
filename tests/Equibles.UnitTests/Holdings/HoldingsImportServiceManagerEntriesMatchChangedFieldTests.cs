using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceManagerEntriesMatchChangedFieldTests
{
    // The failure mode this guards is the opposite of the one the reorder test guards, and it
    // is the dangerous one: reporting "unchanged" for attribution that actually moved means
    // the flush skips the rewrite and the stored breakdown keeps a superseded figure forever,
    // because nothing else in the lane ever revisits a manager entry. An amendment that only
    // re-attributes shares between managers — same count of legs, same names — is exactly the
    // case a comparison written on Count alone, or on the manager names alone, would wave
    // through.
    //
    // Every persisted column except the synthetic key is asserted, so narrowing the comparison
    // to a convenient subset fails here rather than in production a quarter later.
    [Theory]
    [MemberData(nameof(MutatedEntries))]
    public void ManagerEntriesMatch_AnyPersistedFieldDiffers_ReportsChanged(
        HoldingManagerEntry mutated
    )
    {
        var stored = new List<HoldingManagerEntry> { Baseline() };
        var incoming = new List<HoldingManagerEntry> { mutated };

        Invoke(stored, incoming).Should().BeFalse();
    }

    [Fact]
    public void ManagerEntriesMatch_DifferentEntryCount_ReportsChanged()
    {
        var stored = new List<HoldingManagerEntry> { Baseline() };
        var incoming = new List<HoldingManagerEntry> { Baseline(), Baseline() };

        Invoke(stored, incoming).Should().BeFalse();
    }

    public static TheoryData<HoldingManagerEntry> MutatedEntries()
    {
        var data = new TheoryData<HoldingManagerEntry>();

        var managerNumber = Baseline();
        managerNumber.ManagerNumber = 7;
        data.Add(managerNumber);

        var managerName = Baseline();
        managerName.ManagerName = "BETA ADVISORS";
        data.Add(managerName);

        var shared = Baseline();
        shared.SharedManagerNumbers = "4,8,11";
        data.Add(shared);

        var shares = Baseline();
        shares.Shares = 101;
        data.Add(shares);

        var value = Baseline();
        value.Value = 2001;
        data.Add(value);

        var discretion = Baseline();
        discretion.InvestmentDiscretion = InvestmentDiscretion.Defined;
        data.Add(discretion);

        return data;
    }

    private static HoldingManagerEntry Baseline() =>
        new()
        {
            ManagerNumber = 1,
            ManagerName = "ALPHA CAPITAL",
            SharedManagerNumbers = "4,8",
            Shares = 100,
            Value = 2000,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
        };

    private static bool Invoke(List<HoldingManagerEntry> stored, List<HoldingManagerEntry> incoming)
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "ManagerEntriesMatch",
            BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(List<HoldingManagerEntry>), typeof(List<HoldingManagerEntry>)]
        );

        return (bool)method.Invoke(null, [stored, incoming]);
    }
}
