using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class NativeIssuerIdentityEvidenceTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Evidence_PreservesEverySourceField_WithoutALegacyOwner(
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
        foreach (var date in new[] { new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1) })
            DbContext.Add(
                new EquityIssuerTickerEvidence
                {
                    Issuer = issuer,
                    Ticker = "FORMER",
                    FiledDate = date,
                    SourceDocumentId = Guid.NewGuid(),
                    AccessionNumber = $"original-{date.Year}",
                }
            );
        DbContext.Add(
            new IssuerSecurityRegistration
            {
                Issuer = issuer,
                TradingSymbol = "FORMER",
                Title = "Class A Common Stock €",
                ExchangeName = "Original exchange",
                AccessionNumber = "original-registration",
                FiledDate = new DateOnly(2025, 1, 1),
            }
        );
        await DbContext.SaveChangesAsync();
        if (migrateLegacyOwner)
            foreach (var table in new[] { "CommonStockTickerEvidence", "ListedSecurity" })
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
                    .Generate(new RetargetIssuerIdentityEvidence().UpOperations)
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
        (
            await new EquityIssuerTickerEvidenceRepository(DbContext)
                .GetByTickers(["FORMER"])
                .CountAsync()
        )
            .Should()
            .Be(2);
        (
            await new IssuerSecurityRegistrationRepository(DbContext)
                .GetByIssuerId(issuer.Id)
                .SingleAsync()
        )
            .Title.Should()
            .Be("Class A Common Stock €");
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
        foreach (var table in new[] { "CommonStockTickerEvidence", "ListedSecurity" })
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
