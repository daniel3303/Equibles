using System.Data.Common;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class NportHolderCountIndexPostgresTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task EquityIdentityCountUsesTheCoveringIndex()
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.OpenConnectionAsync();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_NportHolding_EquityIdentity' AND i.indisvalid
                """
            )
            .SingleAsync();
        definition.Should().Contain("(\"Isin\", \"Cusip\", \"Lei\", \"NportFilingId\")");

        await using var transaction = await db.Database.BeginTransactionAsync();
        // The migrated test table is empty; make PostgreSQL cost an indexed plan so this
        // verifies that the partial predicate and count can actually use the covering index.
        await db.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            EXPLAIN (COSTS OFF)
            SELECT "Isin", "Cusip", "Lei", count(DISTINCT "NportFilingId")
            FROM "NportHolding"
            WHERE "AssetCategory" = 'EC' AND "Isin" IS NOT NULL
              AND "Isin" = ANY (ARRAY['JE00BV7DQ550', 'JE00B783TY65'])
            GROUP BY "Isin", "Cusip", "Lei"
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(0));
        string.Join('\n', plan)
            .Should()
            .Contain("Index Only Scan using \"IX_NportHolding_EquityIdentity\"");
    }

    [Fact]
    public async Task MigratedDatabase_CoversCusipAndLatestFilingJoin()
    {
        await using var db = fixture.CreateDbContext();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_NportHolding_CusipFiling' AND i.indisvalid
                """
            )
            .SingleAsync();
        // The filing is a key column, so a per-filing probe seeks (Cusip, NportFilingId) instead of
        // walking every entry of a widely held CUSIP.
        definition.Should().Contain("(\"Cusip\", \"NportFilingId\")");
        // Neither the ancient single-column index nor the concurrent replacement's renamed
        // predecessor may survive the migration.
        var leftovers = await db
            .Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM pg_class
                WHERE relname IN ('IX_NportHolding_Cusip', 'IX_NportHolding_CusipFiling_old')
                """
            )
            .SingleAsync();
        leftovers.Should().Be(0);
    }

    // The series-reports read must start from the series' filings and serve its latest-per-period
    // check index-only from the same index, for an issuer-owned series and for a registrant-owned
    // one, instead of from every holding of a widely held CUSIP.
    [Fact]
    public async Task SeriesReportsByPeriod_AreServedByTheSeriesPeriodIndex()
    {
        await using var db = fixture.CreateDbContext();
        var definition = await db
            .Database.SqlQueryRaw<string>(
                """
                SELECT pg_get_indexdef(c.oid) AS "Value"
                FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid
                WHERE c.relname = 'IX_NportFiling_SeriesPeriod' AND i.indisvalid
                """
            )
            .SingleAsync();
        definition
            .Should()
            .Contain(
                "(\"SeriesId\", \"ReportPeriodDate\") INCLUDE (\"EquityIssuerId\", \"RegistrantCik\", \"FilingDate\", \"AccessionNumber\")"
            );

        foreach (
            var (commonStockId, registrantCik) in new[]
            {
                ((Guid?)Guid.NewGuid(), (string)null),
                (null, "0000036405"),
            }
        )
        {
            var plan = await ExplainSeriesReports(commonStockId, registrantCik);
            plan.Split("using \"IX_NportFiling_SeriesPeriod\"")
                .Should()
                .HaveCount(
                    3,
                    "the series scan and its latest-per-period check both use it: {0}",
                    plan
                );
            plan.Should().Contain("Index Only Scan using \"IX_NportFiling_SeriesPeriod\"", plan);
            plan.Should().NotContain("IX_NportFiling_EquityIssuerId_FilingDate", plan);
            plan.Should().NotContain("IX_NportFiling_RegistrantCik", plan);
            // Both scans must seek the series id; a non-sargable predicate would still read the
            // index, but as a full scan with the series id in a Filter.
            plan.Split('\n')
                .Count(line => line.Contains("Index Cond:") && line.Contains("\"SeriesId\""))
                .Should()
                .Be(2, plan);
        }
    }

    // The exact statement the repository sends, planned with sequential and bitmap scans off
    // so only an index the planner cannot use for it is left unused.
    private async Task<string> ExplainSeriesReports(Guid? commonStockId, string registrantCik)
    {
        var capture = new CommandCapture();
        await using var db = fixture.CreateDbContext(options => options.AddInterceptors(capture));
        var reports = await new NportFilingRepository(db)
            .GetSeriesReportsByPeriod(
                commonStockId,
                registrantCik,
                "S000004331",
                new DateOnly(2015, 1, 1)
            )
            .ToListAsync();
        reports.Should().BeEmpty();
        var read = capture
            .Commands.Should()
            .ContainSingle(c => c.Text.Contains("FROM \"NportFiling\""))
            .Subject;

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (
            var scansOff = new NpgsqlCommand(
                "SET enable_seqscan = off; SET enable_bitmapscan = off;",
                connection
            )
        )
            await scansOff.ExecuteNonQueryAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN (COSTS OFF) " + read.Text, connection);
        foreach (var parameter in read.Parameters)
            explain.Parameters.Add(parameter.Clone());
        await using var reader = await explain.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(0));
        return string.Join('\n', plan);
    }

    private sealed record CapturedCommand(string Text, List<NpgsqlParameter> Parameters);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(
                new CapturedCommand(
                    command.CommandText,
                    command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToList()
                )
            );
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
