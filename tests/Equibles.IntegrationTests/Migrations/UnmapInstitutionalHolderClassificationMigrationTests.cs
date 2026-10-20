using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class UnmapInstitutionalHolderClassificationMigrationTests : ParadeDbMcpTestBase
{
    public UnmapInstitutionalHolderClassificationMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // The previous release still maps the column while the rollout replaces its containers, so
    // this release drops only the index, and the column's default fills rows the new model inserts.
    [Fact]
    public async Task Up_KeepsTheColumnForTheRolloutAndDropsOnlyItsIndex()
    {
        new UnmapInstitutionalHolderClassification()
            .UpOperations.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<DropIndexOperation>()
            .Which.Name.Should()
            .Be("IX_InstitutionalHolder_Classification");

        var column = await DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT column_default AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'InstitutionalHolder' AND column_name = 'Classification'
                """
            )
            .SingleAsync();
        column.Should().Be("0", "the release still reading the column needs it, filled by default");

        DbContext.Add(new InstitutionalHolder { Cik = "0000000042", Name = "Unmapped Holder" });
        var insert = () => DbContext.SaveChangesAsync();
        await insert.Should().NotThrowAsync("the default fills the NOT NULL column the model omits");
    }
}
