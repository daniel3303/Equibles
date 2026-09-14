using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class OriginalDirectoryEvidenceTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task OriginalVersions_PreserveDuplicateArraysCheckpointsAndEveryStoredField()
    {
        var source = new CommonStock
        {
            Ticker = "SOURCE",
            Name = "Original source €",
            Description = "Original disclosed text",
            SecondaryTickers = ["SECONDARY", "SECONDARY"],
            SecondaryCiks = ["123", "123"],
            HistoricalCusipBackfillCandidates = ["037833100", "037833100"],
            HistoricalCusipBackfillAmbiguous = true,
            HistoricalCusipBackfillRequestedAt = new DateTime(
                2020,
                1,
                2,
                3,
                4,
                5,
                DateTimeKind.Utc
            ),
            MarketCapitalization = 123456789012345.67,
            SharesOutStanding = 98765432101234,
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var original = await SourceJson(source.Id);
        (await OriginalRecords().SingleAsync()).PayloadJson.Should().Be(original);
        var originalId = (await OriginalRecords().SingleAsync()).Id;
        source.Name = "Changed source";
        source.HistoricalCusipBackfillCandidates = [];
        await DbContext.SaveChangesAsync();
        var changed = await SourceJson(source.Id);
        (await OriginalRecords().CountAsync()).Should().Be(2);
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""DELETE FROM "CommonStock" WHERE "Id" = {source.Id}"""
        );
        DbContext.ChangeTracker.Clear();
        var versions = await OriginalRecords().ToListAsync();
        versions.Should().HaveCount(2);
        versions.Select(row => row.PayloadJson).Should().BeEquivalentTo([original, changed]);
        versions.Single(row => row.Id == originalId).PayloadJson.Should().Be(original);
        versions
            .Should()
            .OnlyContain(row =>
                row.SourceRecordKey == source.Id.ToString() && row.PayloadHash.Length == 64
            );
    }

    [Fact]
    public async Task Capture_IsIndependentOfWriterTimeZoneAndFloatPrecision()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var source = new CommonStock
        {
            Ticker = "CANONICAL",
            MarketCapitalization = 12345.678901234567,
            HistoricalCusipBackfillRequestedAt = new DateTime(
                2020,
                1,
                2,
                3,
                4,
                5,
                DateTimeKind.Utc
            ),
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var original = await SourceJson(source.Id);
        await DbContext.Database.ExecuteSqlRawAsync(
            "SET LOCAL timezone = 'Pacific/Auckland'; SET LOCAL extra_float_digits = -3;"
        );
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "CommonStock" SET "Name" = "Name" WHERE "Id" = {source.Id}"""
        );
        var evidence = await OriginalRecords().AsNoTracking().SingleAsync();
        evidence.PayloadJson.Should().Be(original);
    }

    [Fact]
    public async Task OriginalEvidence_RejectsTruncation()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        DbContext.Add(new CommonStock { Ticker = "NO-TRUNCATE" });
        await DbContext.SaveChangesAsync();
        var before = (await OriginalRecords().SingleAsync()).PayloadJson;
        await transaction.CreateSavepointAsync("before_truncate");
        Func<Task> truncate = () =>
            DbContext.Database.ExecuteSqlRawAsync(
                """TRUNCATE "EquityDirectorySourceRecord" CASCADE;"""
            );
        (await truncate.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RaiseException);
        await transaction.RollbackToSavepointAsync("before_truncate");
        (await OriginalRecords().AsNoTracking().SingleAsync()).PayloadJson.Should().Be(before);
    }

    [Fact]
    public async Task Migration_BackfillsExistingSourceRowsExactly()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var source = new CommonStock
        {
            Ticker = "EXISTING",
            Name = "Retained original source",
            MarketCapitalization = 12345.6789012345,
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var before = await SourceJson(source.Id);
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER equity_directory_source_evidence_immutable ON "EquityDirectorySourceRecord";
            DROP TRIGGER equity_directory_source_evidence_no_truncate ON "EquityDirectorySourceRecord";
            DROP TRIGGER equity_original_directory_evidence ON "CommonStock";
            DROP FUNCTION eq_record_original_directory_change();
            DROP FUNCTION eq_capture_original_directory_record(jsonb);
            DROP FUNCTION eq_protect_directory_source_evidence();
            DELETE FROM "EquityDirectorySourceRecord";
            """
        );
        foreach (
            var operation in new PreserveOriginalDirectoryEvidence().UpOperations.OfType<SqlOperation>()
        )
            await DbContext.Database.ExecuteSqlRawAsync(operation.Sql);
        (await SourceJson(source.Id)).Should().Be(before);
        (await OriginalRecords().SingleAsync()).PayloadJson.Should().Be(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalEvidence_RejectsUpdatesAndDeletes(bool delete)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        DbContext.Add(new CommonStock { Ticker = "IMMUTABLE" });
        await DbContext.SaveChangesAsync();
        var record = await OriginalRecords().SingleAsync();
        await transaction.CreateSavepointAsync("before_mutation");
        Func<Task> mutate = delete
            ? () =>
                DbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""DELETE FROM "EquityDirectorySourceRecord" WHERE "Id" = {record.Id}"""
                )
            : () =>
                DbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""UPDATE "EquityDirectorySourceRecord" SET "PayloadJson" = jsonb_build_object() WHERE "Id" = {record.Id}"""
                );
        (await mutate.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RaiseException);
        await transaction.RollbackToSavepointAsync("before_mutation");
        (await OriginalRecords().AsNoTracking().SingleAsync())
            .PayloadJson.Should()
            .Be(record.PayloadJson);
    }

    private IQueryable<EquityDirectorySourceRecord> OriginalRecords() =>
        DbContext.Set<EquityDirectorySourceRecord>().Where(row => row.Source == "common-stock-v1");

    private Task<string> SourceJson(Guid id) =>
        DbContext
            .Database.SqlQuery<string>(
                $"""
                SELECT to_jsonb(source)::text AS "Value" FROM "CommonStock" source WHERE "Id" = {id}
                """
            )
            .SingleAsync();
}
