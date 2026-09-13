using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeIssuerCorporateActionsTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actions_PreserveEveryField_WithoutALegacyOwner(bool migrateLegacyOwner)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        EquityIssuer issuer;
        if (migrateLegacyOwner)
        {
            var stock = new CommonStock { Name = "Original issuer", Ticker = "ACT" };
            DbContext.Add(stock);
            await DbContext.SaveChangesAsync();
            issuer = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id);
        }
        else
        {
            issuer = new EquityIssuer { Name = "Native issuer" };
            DbContext.Add(issuer);
        }
        var effectiveDate = new DateOnly(2025, 1, 2);
        var captured = new DateTime(2025, 1, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(1234560);
        // Equal dates and unequal ratios preserve distinct exact series and unknown attribution.
        foreach (
            var (ticker, numerator, denominator) in new[]
            {
                ("ACT", 10m, 1m),
                ("ACT-B", 1m, 12m),
                ((string)null, 3m, 2m),
            }
        )
            DbContext.Add(
                new StockSplit
                {
                    Issuer = issuer,
                    PriceSeriesTicker = ticker,
                    EffectiveDate = effectiveDate,
                    Numerator = numerator,
                    Denominator = denominator,
                    Source = StockSplitSource.Manual,
                    CreationTime = captured,
                    PriceAdjustmentAppliedTime = ticker == null ? null : captured,
                }
            );
        DbContext.Add(
            new CashDividend
            {
                Issuer = issuer,
                ExDate = effectiveDate,
                AmountPerShare = 123.456789m,
                Source = CashDividendSource.External,
                CreationTime = captured,
                PriceAdjustmentAppliedAmountPerShare = 122.123456m,
                PriceAdjustmentAppliedTime = captured,
            }
        );
        await DbContext.SaveChangesAsync();
        if (migrateLegacyOwner)
            foreach (var table in new[] { "StockSplit", "CashDividend" })
                await DbContext.Database.ExecuteSqlRawAsync(
                    $"""
                    ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_CommonStockId";
                    ALTER TABLE "{table}" ADD CONSTRAINT "FK_{table}_CommonStock_CommonStockId"
                    FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
                    """
                );
        var before = await Snapshot();
        if (migrateLegacyOwner)
        {
            foreach (
                var command in DbContext
                    .GetService<IMigrationsSqlGenerator>()
                    .Generate(new RetargetCorporateActionsToIssuers().UpOperations)
            )
                await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
            (await Snapshot()).Should().Be(before);
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquityIssuer" SET "CommonStockId" = NULL WHERE "Id" = {issuer.Id};
                DELETE FROM "CommonStock" WHERE "Id" = {issuer.Id};
                """
            );
        }
        (await Snapshot()).Should().Be(before);
        DbContext.ChangeTracker.Clear();
        var splits = await new StockSplitRepository(DbContext).GetByStock(issuer.Id).ToListAsync();
        splits.Should().HaveCount(3);
        splits
            .Single(row => row.PriceSeriesTicker == null)
            .PriceAdjustmentAppliedTime.Should()
            .BeNull();
        var dividends = new CashDividendRepository(DbContext);
        (await dividends.GetByStock(issuer.Id).SingleAsync())
            .AmountPerShare.Should()
            .Be(123.456789m);
        (await dividends.GetPendingPriceAdjustment().CountAsync()).Should().Be(1);
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        Func<Task> deleteIssuer = () =>
            DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""DELETE FROM "EquityIssuer" WHERE "Id" = {issuer.Id}"""
            );
        (await deleteIssuer.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be(PostgresErrorCodes.RestrictViolation);
    }

    private async Task<string> Snapshot()
    {
        var snapshots = new List<string>();
        foreach (var table in new[] { "StockSplit", "CashDividend" })
            snapshots.Add(
                await DbContext
                    .Database.SqlQueryRaw<string>(
                        $"""SELECT COALESCE(jsonb_agg(to_jsonb(row) ORDER BY "Id"), '[]'::jsonb)::text AS "Value" FROM "{table}" row"""
                    )
                    .SingleAsync()
            );
        return string.Join("\n", snapshots);
    }
}
