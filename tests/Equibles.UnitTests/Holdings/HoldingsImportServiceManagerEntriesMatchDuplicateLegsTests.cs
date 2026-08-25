using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceManagerEntriesMatchDuplicateLegsTests
{
    // Order-insensitivity must be multiset equality, not "every incoming leg exists somewhere
    // in the stored set". A filer can report the same manager twice for one position — two
    // legs that merge into one holding — so duplicates are real data, not a parser artefact.
    //
    // Written as a `stored.All(incoming.Contains)` style check, a filing that moved a leg from
    // ALPHA to BETA while keeping the leg count identical would report "unchanged": every
    // remaining ALPHA leg still has a match, and the count agrees. The rewrite would be
    // skipped and the stored breakdown would keep attributing shares to a manager the amended
    // filing no longer credits.
    [Fact]
    public void ManagerEntriesMatch_SameLegCountButOneLegReattributed_ReportsChanged()
    {
        var stored = new List<HoldingManagerEntry> { Entry(1, "ALPHA"), Entry(1, "ALPHA") };
        var incoming = new List<HoldingManagerEntry> { Entry(1, "ALPHA"), Entry(2, "BETA") };

        Invoke(stored, incoming).Should().BeFalse();
    }

    [Fact]
    public void ManagerEntriesMatch_IdenticalDuplicateLegs_ReportsUnchanged()
    {
        var stored = new List<HoldingManagerEntry> { Entry(1, "ALPHA"), Entry(1, "ALPHA") };
        var incoming = new List<HoldingManagerEntry> { Entry(1, "ALPHA"), Entry(1, "ALPHA") };

        Invoke(stored, incoming).Should().BeTrue();
    }

    private static HoldingManagerEntry Entry(int? managerNumber, string managerName) =>
        new()
        {
            ManagerNumber = managerNumber,
            ManagerName = managerName,
            SharedManagerNumbers = null,
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
