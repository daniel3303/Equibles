using Equibles.Congress.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class AddCongressLegislatorTermsMigrationTests(ParadeDbFixture fixture)
{
    private const string BeforeMigration = "20261018025407_AddHolderIndexFundEvidence";

    [Fact]
    public async Task Up_MarksEveryStoredBioguideIdAsCatalogAndLeavesUnresolvedRowsNull()
    {
        await using var database = await IsolatedMigrationDatabase.Create(fixture, BeforeMigration);
        var context = database.Context;
        var identified = new CongressMember { Name = "Ted Budd", BioguideId = "B001305" };
        var unresolved = new CongressMember { Name = "Unknown Filer" };
        context.AddRange(identified, unresolved);
        await context.SaveChangesAsync();

        await context.GetService<IMigrator>().MigrateAsync();

        var sources = await context
            .Database.SqlQueryRaw<SourceRow>(
                """SELECT "Id", "BioguideIdSource" FROM "CongressMember" """
            )
            .ToDictionaryAsync(row => row.Id, row => row.BioguideIdSource);
        sources
            .Should()
            .BeEquivalentTo(
                new Dictionary<Guid, int?>
                {
                    [identified.Id] = (int)CongressBioguideIdSource.Catalog,
                    [unresolved.Id] = null,
                }
            );
    }

    public class SourceRow
    {
        public Guid Id { get; set; }
        public int? BioguideIdSource { get; set; }
    }
}
