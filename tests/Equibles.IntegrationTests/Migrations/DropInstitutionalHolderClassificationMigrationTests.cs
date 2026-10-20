using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class DropInstitutionalHolderClassificationMigrationTests : ParadeDbMcpTestBase
{
    public DropInstitutionalHolderClassificationMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Up_DropsTheUnmappedColumnAndDownRestoresItsDefault()
    {
        var up = new DropInstitutionalHolderClassification().UpOperations;
        up.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SqlOperation>()
            .Which.Sql.Should()
            .Be(DropInstitutionalHolderClassification.DropSql);
        (await ColumnDefault()).Should().BeNull("the migrated schema no longer has the column");

        // Down brings back what the unmap release ran on, and Up drops it again on a retry.
        await DbContext.Database.ExecuteSqlRawAsync(DropInstitutionalHolderClassification.RestoreSql);
        (await ColumnDefault()).Should().Be("0");
        await DbContext.Database.ExecuteSqlRawAsync(DropInstitutionalHolderClassification.DropSql);
        await DbContext.Database.ExecuteSqlRawAsync(DropInstitutionalHolderClassification.DropSql);
        (await ColumnDefault()).Should().BeNull();

        DbContext.Add(new InstitutionalHolder { Cik = "0000000042", Name = "Typed Holder" });
        var insert = () => DbContext.SaveChangesAsync();
        await insert.Should().NotThrowAsync("the model never mapped the dropped column");
    }

    private Task<string> ColumnDefault() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT column_default AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'InstitutionalHolder' AND column_name = 'Classification'
                """
            )
            .SingleOrDefaultAsync();
}
