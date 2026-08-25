using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceManagerEntriesMatchReorderedTests
{
    // The flush path replaces a holding's manager entries by clearing the owned collection
    // and re-adding it, which EF turns into a delete-and-reinsert of every row — and every
    // reinsert draws a fresh value from the owned type's int key sequence. Re-imports
    // re-derive identical attribution for nearly every position they revisit, so doing that
    // unconditionally burned ~45 keys per surviving row and exhausted the sequence, halting
    // the whole 13F lane.
    //
    // The stored side is read back without an ORDER BY, so Postgres may hand the rows back
    // in a different order than the filing was parsed in. Comparing the two as SEQUENCES
    // would then report "changed" for attribution that is in fact identical, the rewrite
    // would happen anyway, and the fix would silently buy nothing while still looking
    // correct in every other test.
    [Fact]
    public void ManagerEntriesMatch_SameEntriesInADifferentOrder_ReportsUnchanged()
    {
        var stored = new List<HoldingManagerEntry>
        {
            Entry(1, "ALPHA CAPITAL", null, 100, 2000),
            Entry(4, "BETA ADVISORS", "4,8", 250, 5000),
        };
        var incoming = new List<HoldingManagerEntry>
        {
            Entry(4, "BETA ADVISORS", "4,8", 250, 5000),
            Entry(1, "ALPHA CAPITAL", null, 100, 2000),
        };

        Invoke(stored, incoming).Should().BeTrue();
    }

    private static HoldingManagerEntry Entry(
        int? managerNumber,
        string managerName,
        string sharedManagerNumbers,
        long shares,
        long value
    ) =>
        new()
        {
            ManagerNumber = managerNumber,
            ManagerName = managerName,
            SharedManagerNumbers = sharedManagerNumbers,
            Shares = shares,
            Value = value,
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
