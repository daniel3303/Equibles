using Equibles.Data;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Testcontainers.PostgreSql;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingReportDateQueriesPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder(
        "postgres:18-alpine"
    ).Build();

    public async Task InitializeAsync() => await _database.StartAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task DateSeeksMatchDistinctAndComposeWithCountOrderingAndLimits()
    {
        await using var db = await CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "InstitutionalHolding" ("ReportDate", "FilingType")
            SELECT d, 0
            FROM unnest(ARRAY[DATE '0001-01-01', DATE '2024-03-31', DATE '2024-09-30', DATE '9999-12-31']) d,
                 generate_series(1, 1000);
            INSERT INTO "InstitutionalHolding" ("ReportDate", "FilingType") VALUES
                ('2024-09-30', 1), ('2024-09-30', 2), ('2024-11-14', 1), ('2024-01-15', 2);
            ANALYZE "InstitutionalHolding";
            """
        );

        var expected = await db
            .Database.SqlQueryRaw<DateOnly>(
                """
                SELECT DISTINCT "ReportDate" AS "Value"
                FROM "InstitutionalHolding" WHERE "FilingType" = 0
                """
            )
            .OrderBy(d => d)
            .ToListAsync();
        var dates = InstitutionalHoldingReportDateQueries.Get13FReportDates(db);

        (await dates.OrderBy(d => d).ToListAsync()).Should().Equal(expected);
        (await dates.CountAsync()).Should().Be(4);
        (await dates.OrderByDescending(d => d).Take(2).ToListAsync())
            .Should()
            .Equal(new DateOnly(9999, 12, 31), new DateOnly(2024, 9, 30));
        (await dates.Where(d => d >= new DateOnly(2024, 1, 1)).CountAsync()).Should().Be(3);

        await using var command = dates.CreateDbCommand();
        command.CommandText = "EXPLAIN (ANALYZE, FORMAT JSON, TIMING OFF) " + command.CommandText;
        var plan = JArray.Parse((string)await command.ExecuteScalarAsync());
        var positionsRead = plan.Descendants()
            .OfType<JObject>()
            .Where(node => (string)node["Relation Name"] == "InstitutionalHolding")
            .Sum(node =>
                ((double)node["Actual Rows"] + ((double?)node["Rows Removed by Filter"] ?? 0))
                * (double)node["Actual Loops"]
            );
        positionsRead.Should().BeLessThan(20, "date discovery must seek past duplicate positions");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrScheduleOnlyHoldingsHaveNo13FReportDates(bool addSchedules)
    {
        await using var db = await CreateContext();
        if (addSchedules)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "InstitutionalHolding" ("ReportDate", "FilingType") VALUES
                    ('2024-03-31', 1), ('2024-09-30', 2)
                """
            );
        }

        var dates = InstitutionalHoldingReportDateQueries.Get13FReportDates(db);
        (await dates.ToListAsync()).Should().BeEmpty();
        (await dates.CountAsync()).Should().Be(0);
    }

    private async Task<EquiblesFinancialDbContext> CreateContext()
    {
        var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseNpgsql(_database.GetConnectionString())
                .Options,
            Array.Empty<IModuleConfiguration>()
        );
        await db.Database.OpenConnectionAsync();
        // The query reads only these columns; a temporary relation permits duplicate dates at
        // volume without inventing thousands of unrelated issuer and holder identities.
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TEMP TABLE "InstitutionalHolding" ("ReportDate" date NOT NULL, "FilingType" integer NOT NULL);
            CREATE INDEX ON "InstitutionalHolding" ("ReportDate");
            """
        );
        return db;
    }
}
