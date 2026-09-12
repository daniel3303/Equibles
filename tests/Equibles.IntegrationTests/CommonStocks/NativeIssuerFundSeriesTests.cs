using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerFundSeriesTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Migration_PreservesEveryField_AndProtectsNativeOwnership()
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        var oldOwner = new CommonStock
        {
            Name = "Fund issuer",
            Ticker = "FUNDS",
            Cik = "0000000096",
        };
        DbContext.Add(oldOwner);
        await DbContext.SaveChangesAsync();
        await DbContext.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "FundSeries" DROP CONSTRAINT "FK_FundSeries_EquityIssuer_CommonStockId";
            """
        );
        var filing = new NportFiling
        {
            EquityIssuerId = oldOwner.Id,
            AccessionNumber = "0000000096-26-000001",
            SeriesId = "S000009600",
        };
        DbContext.Add(filing);
        DbContext.Add(
            new FundSeries
            {
                EquityIssuerId = oldOwner.Id,
                IdentityKey = $"cs:{oldOwner.Id}:S000009600",
                Slug = "existing-public-fund-s000009600",
                SeriesId = "S000009600",
                SeriesName = "Original série €",
                RegistrantName = "Original registrant",
                Ticker = null,
                ClassTickers = ["ORIG-A", "ORIG-B"],
                LatestReportPeriodDate = new DateOnly(2026, 6, 30),
                LatestFilingDate = new DateOnly(2026, 7, 31),
                LatestNportFilingId = filing.Id,
                NetAssets = 123456789.123456789m,
                TotalAssets = 234567890.234567890m,
                PositionCount = 12,
                ReportedHoldingCount = 123,
                FundType = "N-2",
                ComputedAt = new DateTime(2026, 7, 31, 1, 2, 3, DateTimeKind.Utc),
            }
        );
        DbContext.Add(
            new FundSeries
            {
                IdentityKey = "rc:0000000095:S000009500",
                Slug = "unlinked-trust-s000009500",
                RegistrantCik = "0000000095",
                SeriesId = "S000009500",
                LatestNportFilingId = Guid.NewGuid(),
            }
        );
        await DbContext.SaveChangesAsync();
        var before = await Snapshot();
        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new RetargetFundSeriesToIssuers().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
        (await Snapshot()).Should().Be(before);
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "EquityIssuer" SET "CommonStockId" = NULL WHERE "Id" = {oldOwner.Id};
            DELETE FROM "CommonStock" WHERE "Id" = {oldOwner.Id};
            """
        );
        (await Snapshot()).Should().Be(before);
        // Remove the filing first so only FundSeries can refuse the owner deletion.
        await DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM "NportFiling" WHERE "Id" = {filing.Id};
            """
        );
        Func<Task> deleteIssuer = () =>
            DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "EquityIssuer" WHERE "Id" = {oldOwner.Id}"""
            );
        (await deleteIssuer.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RestrictViolation);
    }

    private Task<string> Snapshot() =>
        DbContext
            .Database.SqlQueryRaw<string>(
                """
                SELECT jsonb_agg(to_jsonb(row) ORDER BY "Id")::text AS "Value" FROM "FundSeries" row
                """
            )
            .SingleAsync();
}
