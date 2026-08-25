using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceManagerEntryIdentityCoversEveryColumnTests
{
    // Two contracts depend on ManagerEntryIdentity naming EVERY persisted column of
    // HoldingManagerEntry, and both fail silently if a new column is added without being
    // listed there:
    //
    //  1. A filing that amends only the omitted column would compare equal, the flush would
    //     skip the rewrite, and the stored leg would keep a superseded figure forever —
    //     nothing else in the lane revisits a manager entry.
    //  2. Re-import IS the backfill mechanism for newly added columns on this table. That
    //     works only because a re-parsed leg differs from the stored one while the stored
    //     value is still the default; if the new column is not compared, every leg reads as
    //     unchanged and the column never populates for existing rows.
    //
    // Adding a property to HoldingManagerEntry therefore has to fail here until it is also
    // added to the identity, rather than a quarter later in the data.
    [Fact]
    public void ManagerEntryIdentity_ListsEveryPersistedColumnOfHoldingManagerEntry()
    {
        var identityType = typeof(HoldingsImportService).GetNestedType(
            "ManagerEntryIdentity",
            BindingFlags.NonPublic
        );

        identityType.Should().NotBeNull();

        var persistedColumns = typeof(HoldingManagerEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToList();

        var comparedColumns = identityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToList();

        comparedColumns.Should().Equal(persistedColumns);
    }
}
