using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeIssuerHoldingsTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly string[] SummaryTables =
    [
        "StockQuarterlyActivity",
        "StockQuarterlyActivityCombined",
        "StockQuarterlyListingActivity",
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Holdings_PreserveAllFieldsAndManagerLegs_WithoutALegacyOwner(
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
            issuer = new EquityIssuer { Name = "Native issuer without listing" };
            DbContext.Add(issuer);
        }
        var holder = new InstitutionalHolder { Name = "Original manager", Cik = "0001234567" };
        var date = new DateOnly(2025, 3, 31);
        var captured = new DateTime(2025, 5, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560);
        foreach (var ticker in new string[] { null, "FORMER", "CLASS-B" })
            DbContext.Add(
                new InstitutionalHolding
                {
                    Issuer = issuer,
                    InstitutionalHolder = holder,
                    ListedTicker = ticker,
                    Cusip = "123456789",
                    TitleOfClass = "Original class €",
                    ReportDate = date,
                    FilingDate = date.AddDays(40),
                    AccessionNumber = "original-accession",
                    Shares = 987654321,
                    Value = long.MaxValue,
                    FiledValue = 123456789012345,
                    ShareType = ticker == "CLASS-B" ? ShareType.Principal : ShareType.Shares,
                    OptionType = ticker == "FORMER" ? OptionType.Call : null,
                    InvestmentDiscretion = InvestmentDiscretion.Defined,
                    FilingType = FilingType.Form13F,
                    VotingAuthSole = 123,
                    VotingAuthShared = 456,
                    VotingAuthNone = 789,
                    PercentOfClass = 12.3456m,
                    IsAmendment = true,
                    ValuePending = true,
                    ValueRetryCount = 3,
                    ValueLastRetryAt = captured,
                    ValueSource = ValueSource.Filed,
                    ValueUnavailable = true,
                    CreationTime = captured,
                    ManagerEntries =
                    [
                        new()
                        {
                            ManagerNumber = 4,
                            ManagerName = "Joint manager",
                            SharedManagerNumbers = "4,8,11",
                            Shares = 456,
                            Value = 987,
                            InvestmentDiscretion = InvestmentDiscretion.Other,
                        },
                        new()
                        {
                            ManagerNumber = null,
                            ManagerName = "Unattributed manager",
                            Shares = 123,
                            Value = 654,
                            InvestmentDiscretion = InvestmentDiscretion.Sole,
                        },
                    ],
                }
            );
        DbContext.Add(
            new StockQuarterlyActivity
            {
                EquityIssuerId = issuer.Id,
                ReportDate = date,
                PreviousReportDate = date.AddMonths(-3),
                CurrentShares = 123,
                PreviousShares = 456,
                CurrentValue = 789,
                PreviousValue = 987,
                CurrentFilerCount = 12,
                PreviousFilerCount = 34,
                NewFilerCount = 5,
                SoldOutFilerCount = 6,
                ComputedAt = captured,
            }
        );
        DbContext.Add(
            new StockQuarterlyActivityCombined
            {
                EquityIssuerId = issuer.Id,
                ReportDate = date,
                PreviousReportDate = date.AddMonths(-3),
                CurrentShares = 321,
                PreviousShares = 654,
                CurrentValue = 987,
                PreviousValue = 789,
                CurrentFilerCount = 21,
                PreviousFilerCount = 43,
                NewFilerCount = 6,
                SoldOutFilerCount = 5,
                ComputedAt = captured,
            }
        );
        foreach (var combined in new[] { false, true })
            DbContext.Add(
                new StockQuarterlyListingActivity
                {
                    EquityIssuerId = issuer.Id,
                    ReportDate = date,
                    IsCombined = combined,
                    PriceSeriesTicker = "FORMER",
                    CurrentShares = 234,
                    PreviousShares = 567,
                    ComputedAt = captured,
                }
            );
        await DbContext.SaveChangesAsync();
        var orphanId = Guid.NewGuid();
        if (migrateLegacyOwner)
        {
            await DbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE "InstitutionalHolding" DROP CONSTRAINT "FK_InstitutionalHolding_EquityIssuer_CommonStockId";
                ALTER TABLE "InstitutionalHolding" ADD CONSTRAINT "FK_InstitutionalHolding_CommonStock_CommonStockId"
                FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
                """
            );
            foreach (var table in SummaryTables)
                await DbContext.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_CommonStockId"; ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_EquityIssuerId";"""
                );
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "StockQuarterlyActivity" ("CommonStockId", "ReportDate", "PreviousReportDate", "CurrentShares", "PreviousShares", "CurrentValue", "PreviousValue", "CurrentFilerCount", "PreviousFilerCount", "NewFilerCount", "SoldOutFilerCount", "ComputedAt") SELECT {orphanId}, "ReportDate", "PreviousReportDate", "CurrentShares", "PreviousShares", "CurrentValue", "PreviousValue", "CurrentFilerCount", "PreviousFilerCount", "NewFilerCount", "SoldOutFilerCount", "ComputedAt"
                FROM "StockQuarterlyActivity" WHERE "CommonStockId" = {issuer.Id};
                """
            );
        }
        var before = await Snapshot();
        if (migrateLegacyOwner)
        {
            foreach (
                var command in DbContext
                    .GetService<IMigrationsSqlGenerator>()
                    .Generate(new RetargetHoldingsToIssuers().UpOperations)
            )
                await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);
            (await Snapshot()).Should().Be(before);
            var retained = await DbContext
                .Set<EquityIssuer>()
                .SingleAsync(row => row.Id == orphanId);
            retained.Name.Should().BeNull();
            DbContext
                .Entry(retained)
                .Property<Guid?>("CommonStockId")
                .CurrentValue.Should()
                .BeNull();
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquityIssuer" SET "CommonStockId" = NULL WHERE "Id" = {issuer.Id};
                DELETE FROM "CommonStock" WHERE "Id" = {issuer.Id};
                """
            );
        }
        (await Snapshot()).Should().Be(before);
        DbContext.ChangeTracker.Clear();
        (
            await DbContext
                .Set<InstitutionalHolding>()
                .Where(row => row.EquityIssuerId == issuer.Id)
                .Select(row => row.Issuer.Name)
                .Distinct()
                .SingleAsync()
        )
            .Should()
            .Be(issuer.Name);
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
            var table in new[] { "InstitutionalHolding", "HoldingManagerEntry" }.Concat(
                SummaryTables
            )
        )
            snapshots.Add(
                await DbContext
                    .Database.SqlQueryRaw<string>(
                        $"""SELECT COALESCE(jsonb_agg(to_jsonb(row) ORDER BY to_jsonb(row)), '[]'::jsonb)::text AS "Value" FROM "{table}" row"""
                    )
                    .SingleAsync()
            );
        return string.Join("\n", snapshots);
    }
}
