using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerCongressionalTradesTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Trades_PreserveFiledEvidenceAndUnresolvedRows_WithoutALegacyOwner(
        bool migrateLegacyOwner
    )
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        EquityIssuer issuer;
        if (migrateLegacyOwner)
        {
            var stock = new CommonStock { Name = "Original issuer", Ticker = "CURRENT" };
            DbContext.Add(stock);
            await DbContext.SaveChangesAsync();
            issuer = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id);
        }
        else
        {
            issuer = new EquityIssuer { Name = "Native issuer" };
            DbContext.Add(issuer);
        }
        var member = new CongressMember { Name = "Original member", StateDistrict = "SC05" };
        var captured = new DateTime(2025, 1, 3, 4, 5, 6, DateTimeKind.Utc).AddTicks(1234560);
        foreach (
            var (owner, ticker, index) in new[]
            {
                (issuer, "FORMER", 0),
                ((EquityIssuer)null, "AMBIGUOUS", 2),
                (issuer, "", 3),
            }
        )
            DbContext.Add(
                new CongressionalTrade
                {
                    Issuer = owner,
                    CongressMember = member,
                    FiledTicker = ticker,
                    FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport,
                    SourceId = "original-source",
                    SourceRowIndex = index,
                    TransactionDate = new DateOnly(2024, 12, 23),
                    FilingDate = new DateOnly(2025, 1, 2),
                    TransactionType = CongressTransactionType.Purchase,
                    OwnerType = "Spouse",
                    AssetName = "Original asset €",
                    AssetType = "ST",
                    Subholding = "Original account",
                    AmountFrom = 1001,
                    AmountTo = long.MaxValue,
                    CreationTime = captured,
                }
            );
        DbContext.Add(
            new CongressionalTradeImportPartition
            {
                Kind = CongressionalFilingKind.HousePeriodicTransactionReport,
                Year = 2024,
                ParserVersion = 6,
                FilingCount = 1,
                TransactionCount = 3,
                CompletionTime = captured,
            }
        );
        await DbContext.SaveChangesAsync();
        if (migrateLegacyOwner)
            await DbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "CongressionalTrade" DROP CONSTRAINT "FK_CongressionalTrade_EquityIssuer_CommonStockId";
                ALTER TABLE "CongressionalTrade" ADD CONSTRAINT "FK_CongressionalTrade_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE SET NULL;
                """
            );
        var before = await Snapshot();
        if (migrateLegacyOwner)
        {
            foreach (
                var command in DbContext
                    .GetService<IMigrationsSqlGenerator>()
                    .Generate(new RetargetCongressionalTradesToIssuers().UpOperations)
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
        var trades = await new CongressionalTradeRepository(DbContext)
            .GetByMember(member)
            .ToListAsync();
        trades.Should().HaveCount(3);
        trades.Single(row => row.FiledTicker == "AMBIGUOUS").EquityIssuerId.Should().BeNull();
        trades.Single(row => row.FiledTicker == "FORMER").EquityIssuerId.Should().Be(issuer.Id);
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
        foreach (
            var table in new[]
            {
                "CongressionalTrade",
                "CongressMember",
                "CongressionalTradeImportPartition",
            }
        )
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
