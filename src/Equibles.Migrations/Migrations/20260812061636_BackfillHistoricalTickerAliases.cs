using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BackfillHistoricalTickerAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "CommonStockTickerAlias" ("Id", "CommonStockId", "Ticker", "CreationTime")
                SELECT historical."Id", stock."Id", historical."Ticker", CURRENT_TIMESTAMP
                FROM (VALUES
                    ('70700000-0000-4000-8000-000000000001'::uuid, 'SNBR', '827187'),
                    ('70700000-0000-4000-8000-000000000002'::uuid, 'GOCO', '1808220'),
                    ('70700000-0000-4000-8000-000000000003'::uuid, 'SATS', '1415404'),
                    ('70700000-0000-4000-8000-000000000004'::uuid, 'SSSS', '1509470'),
                    ('70700000-0000-4000-8000-000000000005'::uuid, 'SKLZ', '1801661'),
                    ('70700000-0000-4000-8000-000000000006'::uuid, 'SCVL', '895447'),
                    ('70700000-0000-4000-8000-000000000007'::uuid, 'IAC', '1800227'),
                    ('70700000-0000-4000-8000-000000000008'::uuid, 'NOTV', '720154'),
                    ('70700000-0000-4000-8000-000000000009'::uuid, 'ATLN', '1605888'),
                    ('70700000-0000-4000-8000-000000000010'::uuid, 'BK', '1390777'),
                    ('70700000-0000-4000-8000-000000000011'::uuid, 'CGCT', '2049662'),
                    ('70700000-0000-4000-8000-000000000012'::uuid, 'LOKV', '2048951'),
                    ('70700000-0000-4000-8000-000000000013'::uuid, 'SGMO', '1001233'),
                    ('70700000-0000-4000-8000-000000000014'::uuid, 'USEG', '101594'),
                    ('70700000-0000-4000-8000-000000000015'::uuid, 'XWIN', '1473334'),
                    ('70700000-0000-4000-8000-000000000016'::uuid, 'WTO', '1789299'),
                    ('70700000-0000-4000-8000-000000000017'::uuid, 'AGH', '2009312'),
                    ('70700000-0000-4000-8000-000000000018'::uuid, 'CGEH', '1009759'),
                    ('70700000-0000-4000-8000-000000000019'::uuid, 'DEVS', '1854480'),
                    ('70700000-0000-4000-8000-000000000020'::uuid, 'SUUN', '2011053'),
                    ('70700000-0000-4000-8000-000000000021'::uuid, 'ZBAI', '1755058')
                ) AS historical("Id", "Ticker", "Cik")
                JOIN "CommonStock" AS stock
                    ON regexp_replace(stock."Cik", '^0+', '') = historical."Cik"
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "CommonStock" AS live_stock
                    WHERE live_stock."Ticker" = historical."Ticker"
                        OR historical."Ticker" = ANY(
                            coalesce(live_stock."SecondaryTickers", ARRAY[]::text[])
                        )
                        OR historical."Ticker" = ANY(
                            coalesce(live_stock."ReferenceTickers", ARRAY[]::text[])
                        )
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM "CommonStock" AS duplicate_owner
                    WHERE duplicate_owner."Id" <> stock."Id"
                        AND regexp_replace(duplicate_owner."Cik", '^0+', '') = historical."Cik"
                )
                ON CONFLICT DO NOTHING;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "CommonStockTickerAlias" AS target
                USING (VALUES
                    ('70700000-0000-4000-8000-000000000001'::uuid, 'SNBR', '827187'),
                    ('70700000-0000-4000-8000-000000000002'::uuid, 'GOCO', '1808220'),
                    ('70700000-0000-4000-8000-000000000003'::uuid, 'SATS', '1415404'),
                    ('70700000-0000-4000-8000-000000000004'::uuid, 'SSSS', '1509470'),
                    ('70700000-0000-4000-8000-000000000005'::uuid, 'SKLZ', '1801661'),
                    ('70700000-0000-4000-8000-000000000006'::uuid, 'SCVL', '895447'),
                    ('70700000-0000-4000-8000-000000000007'::uuid, 'IAC', '1800227'),
                    ('70700000-0000-4000-8000-000000000008'::uuid, 'NOTV', '720154'),
                    ('70700000-0000-4000-8000-000000000009'::uuid, 'ATLN', '1605888'),
                    ('70700000-0000-4000-8000-000000000010'::uuid, 'BK', '1390777'),
                    ('70700000-0000-4000-8000-000000000011'::uuid, 'CGCT', '2049662'),
                    ('70700000-0000-4000-8000-000000000012'::uuid, 'LOKV', '2048951'),
                    ('70700000-0000-4000-8000-000000000013'::uuid, 'SGMO', '1001233'),
                    ('70700000-0000-4000-8000-000000000014'::uuid, 'USEG', '101594'),
                    ('70700000-0000-4000-8000-000000000015'::uuid, 'XWIN', '1473334'),
                    ('70700000-0000-4000-8000-000000000016'::uuid, 'WTO', '1789299'),
                    ('70700000-0000-4000-8000-000000000017'::uuid, 'AGH', '2009312'),
                    ('70700000-0000-4000-8000-000000000018'::uuid, 'CGEH', '1009759'),
                    ('70700000-0000-4000-8000-000000000019'::uuid, 'DEVS', '1854480'),
                    ('70700000-0000-4000-8000-000000000020'::uuid, 'SUUN', '2011053'),
                    ('70700000-0000-4000-8000-000000000021'::uuid, 'ZBAI', '1755058')
                ) AS historical("Id", "Ticker", "Cik"),
                "CommonStock" AS stock
                WHERE target."Id" = historical."Id"
                    AND target."Ticker" = historical."Ticker"
                    AND stock."Id" = target."CommonStockId"
                    AND regexp_replace(stock."Cik", '^0+', '') = historical."Cik";
                """
            );
        }
    }
}
