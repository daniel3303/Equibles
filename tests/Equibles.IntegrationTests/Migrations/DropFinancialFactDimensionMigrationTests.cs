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

        // Longer than one 3 s attempt, so the first one has been refused.
        await Task.Delay(TimeSpan.FromSeconds(4));
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
