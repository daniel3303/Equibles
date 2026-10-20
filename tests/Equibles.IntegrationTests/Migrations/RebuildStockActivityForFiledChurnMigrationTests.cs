using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class RebuildStockActivityForFiledChurnMigrationTests : ParadeDbMcpTestBase
{
    public RebuildStockActivityForFiledChurnMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // A clean quarter, a pending one and one under a drain lease all become due for a rebuild
    // after the cooldown; the lease cannot be cleared by a rebuild that started before the stamp.
    [Fact]
    public async Task Up_StampsEveryQuarterForTheDrain()
    {
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

        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new RebuildStockActivityForFiledChurn().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);

        var stamps = await DbContext
            .Set<AumQuarterlySnapshot>()
            .AsNoTracking()
            .Select(s => s.DirtyAt)
            .ToListAsync();
        stamps.Should().HaveCount(3);
        stamps.Should().OnlyContain(stamp => stamp > before && stamp < lease);
    }
}
