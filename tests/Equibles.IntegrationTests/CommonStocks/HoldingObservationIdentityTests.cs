using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class HoldingObservationIdentityTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(null, "ORIGINAL")]
    [InlineData("ORIGINAL", null)]
    public async Task RetiringWriter_RefusesChangedIdentityBeforeItCanReattachManagers(
        string retainedTicker,
        string retiringTicker
    )
    {
        var source = new CommonStock { Ticker = "ORIGINAL", Cusip = "123456789" };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        var issuer = await DbContext.Set<EquityIssuer>().SingleAsync();
        var holder = new InstitutionalHolder { Cik = "0000000999", Name = "Filing manager" };
        var position = new InstitutionalHolding
        {
            Issuer = issuer,
            InstitutionalHolder = holder,
            ReportDate = new(2026, 6, 30),
            FilingDate = new(2026, 8, 1),
            Cusip = source.Cusip,
            AccessionNumber = "retained-observation",
            ListedTicker = retainedTicker,
            Shares = 100,
            Value = 2000,
            FiledValue = 2000,
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    Shares = 100,
                    Value = 2000,
                },
            ],
        };
        var option = new InstitutionalHolding
        {
            Issuer = issuer,
            InstitutionalHolder = holder,
            ReportDate = position.ReportDate,
            FilingDate = position.FilingDate,
            Cusip = source.Cusip,
            AccessionNumber = position.AccessionNumber,
            ListedTicker = retainedTicker,
            OptionType = OptionType.Put,
            Shares = 30,
            Value = 90,
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 2,
                    Shares = 30,
                    Value = 90,
                },
            ],
        };
        // The retiring process would attach its managers to this other security if only
        // its upsert key were redirected. Refuse the write before that second phase.
        var otherSecurity = new InstitutionalHolding
        {
            Issuer = issuer,
            InstitutionalHolder = holder,
            ReportDate = position.ReportDate,
            FilingDate = position.FilingDate,
            Cusip = "987654321",
            AccessionNumber = position.AccessionNumber,
            ListedTicker = retiringTicker,
            Shares = 999,
            Value = 9999,
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 99,
                    Shares = 999,
                    Value = 9999,
                },
            ],
        };
        DbContext.AddRange(position, option, otherSecurity);
        await DbContext.SaveChangesAsync();
        var originalRows = await DbContext
            .Database.SqlQuery<string>(
                $"""
                SELECT jsonb_agg(to_jsonb(h) ORDER BY "Id")::text AS "Value" FROM "InstitutionalHolding" h
                """
            )
            .SingleAsync();
        var originalLegs = await DbContext
            .Database.SqlQuery<string>(
                $"""
                SELECT jsonb_agg(to_jsonb(m) ORDER BY "Id")::text AS "Value" FROM "HoldingManagerEntry" m
                """
            )
            .SingleAsync();
        // The old binary provides only CommonStockId and its old ticker interpretation.
        // Refusal must stop its accession-wide manager reattachment phase as well as its upsert.
        Func<Task> retiringReplay = async () =>
        {
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "InstitutionalHolding"
                SELECT r.* FROM "InstitutionalHolding" h
                CROSS JOIN LATERAL jsonb_populate_record(NULL::"InstitutionalHolding", to_jsonb(h)
                    || jsonb_build_object('Id', gen_random_uuid(), 'EquityIssuerId', NULL,
                        'ListedTicker', {retiringTicker}::text)) r
                WHERE h."Cusip" = '123456789'
                ON CONFLICT ("CommonStockId", "InstitutionalHolderId", "ReportDate", "ShareType", "OptionType", "FilingType", "ListedTicker")
                DO UPDATE SET "Value" = EXCLUDED."Value"
                """
            );
            await DbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "HoldingManagerEntry" m SET "Shares" = 100
                FROM "InstitutionalHolding" h
                WHERE h."Id" = m."InstitutionalHoldingId" AND h."AccessionNumber" = {position.AccessionNumber}
                  AND h."ListedTicker" IS NOT DISTINCT FROM {retiringTicker}::text
                """
            );
        };
        (await retiringReplay.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should()
            .Be("23514");
        (
            await DbContext
                .Database.SqlQuery<string>(
                    $"""
                    SELECT jsonb_agg(to_jsonb(h) ORDER BY "Id")::text AS "Value" FROM "InstitutionalHolding" h
                    """
                )
                .SingleAsync()
        ).Should().Be(originalRows);
        (
            await DbContext
                .Database.SqlQuery<string>(
                    $"""
                    SELECT jsonb_agg(to_jsonb(m) ORDER BY "Id")::text AS "Value" FROM "HoldingManagerEntry" m
                    """
                )
                .SingleAsync()
        ).Should().Be(originalLegs);
    }
}
