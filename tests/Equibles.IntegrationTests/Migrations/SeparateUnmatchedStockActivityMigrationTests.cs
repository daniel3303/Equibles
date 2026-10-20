using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class SeparateUnmatchedStockActivityMigrationTests : ParadeDbMcpTestBase
{
    public SeparateUnmatchedStockActivityMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // The new columns start at zero, so every quarter is stamped for the drain to refill them;
    // a pending stamp and a drain lease are replaced too.
    [Fact]
    public async Task Up_AddsTheColumnsThenStampsEveryQuarterForTheDrain()
    {
        var operations = new SeparateUnmatchedStockActivity().UpOperations;
        operations
            .OfType<AddColumnOperation>()
            .Select(op => $"{op.Table}.{op.Name}")
            .Should()
            .BeEquivalentTo(
                "StockQuarterlyActivity.UnmatchedCurrentShares",
                "StockQuarterlyActivity.UnmatchedPreviousShares",
                "StockQuarterlyActivity.UnmatchedCurrentValue",
                "StockQuarterlyActivity.UnmatchedPreviousValue",
                "StockQuarterlyListingActivity.UnmatchedCurrentShares",
                "StockQuarterlyListingActivity.UnmatchedPreviousShares",
                "StockQuarterlyActivityCombined.UnmatchedCurrentShares",
                "StockQuarterlyActivityCombined.UnmatchedPreviousShares",
                "StockQuarterlyActivityCombined.UnmatchedCurrentValue",
                "StockQuarterlyActivityCombined.UnmatchedPreviousValue"
            );
        operations.Last().Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Be(SeparateUnmatchedStockActivity.RepairSql);

        var lease = DateTime.UtcNow.AddMinutes(5);
        DbContext.AddRange(
            new AumQuarterlySnapshot { ReportDate = new DateOnly(2002, 3, 31) },
            new AumQuarterlySnapshot
            {
                ReportDate = new DateOnly(2026, 3, 31),
                DirtyAt = DateTime.UtcNow.AddHours(-3),
            },
            new AumQuarterlySnapshot { ReportDate = new DateOnly(2026, 6, 30), DirtyAt = lease }
        );
        await DbContext.SaveChangesAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);

        await DbContext.Database.ExecuteSqlRawAsync(SeparateUnmatchedStockActivity.RepairSql);

        var stamps = await DbContext
            .Set<AumQuarterlySnapshot>()
            .AsNoTracking()
            .Select(s => s.DirtyAt)
            .ToListAsync();
        stamps.Should().HaveCount(3);
        stamps.Should().OnlyContain(stamp => stamp > before && stamp < lease);
    }
}
