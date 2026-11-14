using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class DropFinancialFactDimensionMigrationTests : ParadeDbMcpTestBase
{
    public DropFinancialFactDimensionMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Up_DropsTheUnmappedTableAndDownRestoresItEmpty()
    {
        var up = new DropFinancialFactDimension().UpOperations;
        up.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<SqlOperation>()
            .Which.Sql.Should()
            .Be(DropFinancialFactDimension.DropSql);
        (await TableExists()).Should().BeFalse("the migrated schema no longer has the table");

        // Down brings back what the unmap release ran on, and Up drops it again on a retry.
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.RestoreSql);
        (await TableExists()).Should().BeTrue();
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.DropSql);
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.DropSql);
        (await TableExists()).Should().BeFalse();
    }

    // A fact read in flight holds a lock the drop conflicts with; the drop must wait it out in
    // short attempts and still finish, never fail on the first refusal.
    [Fact]
    public async Task Up_WhileAFactReadHoldsItsLock_RetriesUntilTheReadEnds()
    {
        await DbContext.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.RestoreSql);
        await using var reader = Fixture.CreateDbContext();
        await using var read = await reader.Database.BeginTransactionAsync();
        await reader.Database.ExecuteSqlRawAsync(
            """LOCK TABLE "FinancialFact" IN ACCESS SHARE MODE"""
        );

        await using var dropper = Fixture.CreateDbContext();
        var dropping = dropper.Database.ExecuteSqlRawAsync(DropFinancialFactDimension.DropSql);

        // Between attempts the drop sleeps, and a refused attempt has given its locks back.
        var sleeping = false;
        for (var poll = 0; poll < 100 && !sleeping; poll++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
            sleeping = await DbContext
                .Database.SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity AS activity
                        WHERE activity.wait_event = 'PgSleep'
                            AND activity.query LIKE '%DROP TABLE "FinancialFactDimension"%'
                            AND activity.pid <> pg_backend_pid()
                            AND NOT EXISTS (
                                SELECT 1
                                FROM pg_locks AS held
                                WHERE held.pid = activity.pid
                                    AND held.relation = '"FinancialFact"'::regclass
                            )
                    ) AS "Value"
                    """
                )
                .SingleAsync();
        }
        sleeping.Should().BeTrue("the first attempt is refused after its lock timeout");
        dropping.IsCompleted.Should().BeFalse("the read still holds its lock");
        (await TableExists()).Should().BeTrue();

        await read.CommitAsync();
        await dropping;
        (await TableExists()).Should().BeFalse();
    }

    private async Task<bool> TableExists() =>
        await DbContext
            .Database.SqlQueryRaw<bool>(
                """SELECT to_regclass('"FinancialFactDimension"') IS NOT NULL AS "Value" """
            )
            .SingleAsync();
}
