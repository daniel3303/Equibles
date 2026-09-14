using Equibles.CommonStocks.Data.Models;
using Equibles.FdaCatalysts.Data.Models;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeIssuerDisclosureTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disclosures_PreserveEveryStoredField_WithoutLegacyOwnership(bool migrate)
    {
        await using var transaction = await DbContext.Database.BeginTransactionAsync();
        EquityIssuer issuer;
        if (migrate)
        {
            var stock = new CommonStock
            {
                Name = "Disclosure issuer",
                Ticker = "DISCL",
                Cik = "0000000092",
            };
            DbContext.Add(stock);
            await DbContext.SaveChangesAsync();
            issuer = await DbContext.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id);
        }
        else
        {
            issuer = new EquityIssuer { Name = "Native disclosure issuer", Cik = "0000000092" };
            DbContext.Add(issuer);
        }
        var owner = new InsiderOwner
        {
            Name = "Original owner",
            OwnerCik = "0000000091",
            IsOfficer = true,
        };
        DbContext.Add(
            new InsiderTransaction
            {
                Issuer = issuer,
                InsiderOwner = owner,
                AccessionNumber = "0000000092-26-000001",
                TransactionOrder = 3,
                FilingDate = new DateOnly(2026, 8, 2),
                TransactionDate = new DateOnly(2026, 8, 1),
                Shares = 1234,
                SharesOwnedAfter = 98765,
                PricePerShare = 123.123456789m,
                ReportedPricePerShare = 987.987654321m,
                PriceWasRepaired = true,
                IsPriceValid = false,
                IsRule10b5One = true,
                Notes = ["Original evidence €", "Second source note"],
                SecurityTitle = "Source-stated receipt",
                IsAmendment = true,
                OriginalFilingDate = new DateOnly(2026, 7, 31),
                SupersededAccessionNumber = "0000000092-26-000000",
            }
        );
        DbContext.Add(
            new Form144Filing
            {
                Issuer = issuer,
                AccessionNumber = "0000000092-26-000002",
                FilingDate = new DateOnly(2026, 8, 2),
                FilerCik = owner.OwnerCik,
                SellerName = owner.Name,
                SharesToBeSold = 4567,
                AggregateMarketValue = 123456.123456789m,
                SharesOutstanding = 7654321,
                ApproxSaleDate = new DateOnly(2026, 8, 10),
                SecuritiesExchangeName = "Original reported exchange",
                SecurityClassTitle = "Original security class",
                FilerCikBackfillAttempts = 2,
                FilerCikBackfillAttemptedAt = new DateTime(2026, 8, 3, 1, 2, 3, DateTimeKind.Utc),
                PriorSales =
                [
                    new Form144PriorSale
                    {
                        SellerName = "Original prior seller",
                        SecurityClassTitle = "Original prior class",
                        SaleDate = new DateOnly(2026, 6, 30),
                        AmountSold = 123,
                        GrossProceeds = 2345.123456789m,
                    },
                ],
            }
        );
        DbContext.Add(
            new GovernmentContract
            {
                Issuer = issuer,
                AwardUniqueKey = "original-award-key",
                RecipientName = "Original recipient €",
                Amount = 987654321.123456789m,
                TotalOutlays = 123456789.987654321m,
                ActionDate = new DateOnly(2026, 7, 31),
                Description = "Original\nsource description",
            }
        );
        DbContext.Add(
            new FdaCatalyst
            {
                Issuer = issuer,
                Title = "Original linked event",
                Center = "Source center",
                SourceReference = "original-linked-event",
                MeetingDate = new DateOnly(2026, 9, 30),
            }
        );
        DbContext.Add(
            new FdaCatalyst
            {
                Title = "Original unresolved event",
                Center = "Source center",
                SourceReference = "original-unresolved-event",
                MeetingDate = new DateOnly(2026, 9, 29),
            }
        );
        await DbContext.SaveChangesAsync();
        if (migrate)
        {
            foreach (var table in OwnerTables)
            {
                await DbContext.Database.ExecuteSqlRawAsync(
                    $"""ALTER TABLE "{table}" DROP CONSTRAINT "FK_{table}_EquityIssuer_CommonStockId";"""
                );
                if (table != "FdaCatalyst")
                    await DbContext.Database.ExecuteSqlRawAsync(
                        $"""
                        ALTER TABLE "{table}" ADD CONSTRAINT "FK_{table}_CommonStock_CommonStockId"
                        FOREIGN KEY ("CommonStockId") REFERENCES "CommonStock"("Id") ON DELETE CASCADE;
                        """
                    );
            }
        }
        var before = await Snapshot();
        if (migrate)
        {
            foreach (
                var command in DbContext
                    .GetService<IMigrationsSqlGenerator>()
                    .Generate(new RetargetIssuerDisclosures().UpOperations)
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
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(0);
        foreach (var table in OwnerTables)
        {
            var name = $"FK_{table}_EquityIssuer_CommonStockId";
            var protectedOwner = await DbContext
                .Database.SqlQuery<bool>(
                    $"""
                    SELECT convalidated AND confdeltype = 'r' AS "Value" FROM pg_constraint
                    WHERE conname = {name} AND confrelid = '"EquityIssuer"'::regclass
                    """
                )
                .SingleAsync();
            protectedOwner.Should().BeTrue();
        }
    }

    private static readonly string[] OwnerTables =
    [
        "InsiderTransaction",
        "Form144Filing",
        "GovernmentContract",
        "FdaCatalyst",
    ];

    private async Task<string> Snapshot()
    {
        var rows = new List<string>();
        foreach (var table in OwnerTables.Concat(["InsiderOwner", "Form144PriorSale"]))
            rows.Add(
                await DbContext
                    .Database.SqlQueryRaw<string>(
                        $"""SELECT jsonb_agg(to_jsonb(row) ORDER BY "Id")::text AS "Value" FROM "{table}" row"""
                    )
                    .SingleAsync()
            );
        return string.Join("\n", rows);
    }
}
