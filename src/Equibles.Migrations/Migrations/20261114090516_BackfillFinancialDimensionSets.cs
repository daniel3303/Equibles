using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class BackfillFinancialDimensionSets : Migration
    {
        // Facts stored before the dimension sets existed state their (axis, member) pairs only as
        // per-fact rows, which no reader uses any more and the next migration drops. Each key
        // without a set gets one from a fact whose rows hash to the key, exactly as the extractor
        // computes it (SHA-256 of the ordinal-sorted "axis=member" pairs joined by "|"), so a set
        // is never built from a fact whose rows were cut short. A key no fact states in full
        // stays without a set until its filing is extracted again.
        private const string StatesItsKey = """
            fact."DimensionsKey" = (
                SELECT encode(
                    sha256(convert_to(string_agg(pairs."Pair", '|' ORDER BY pairs."Pair" COLLATE "C"), 'UTF8')),
                    'hex'
                )
                FROM (
                    SELECT stated."Axis" || '=' || stated."Member" AS "Pair"
                    FROM "FinancialFactDimension" AS stated
                    WHERE stated."FinancialFactId" = fact."Id"
                ) AS pairs
            )
            """;

        public const string BackfillSql = $$"""
            DO $$
            BEGIN
                IF to_regclass('"FinancialFactDimension"') IS NULL THEN
                    RETURN;
                END IF;

                -- One fact for each key that has no set yet.
                CREATE TEMP TABLE "DimensionSetCandidate" ON COMMIT DROP AS
                SELECT fact."DimensionsKey" AS "Key", min(fact."Id"::text)::uuid AS "FactId"
                FROM "FinancialFact" AS fact
                WHERE fact."DimensionsKey" <> ''
                    AND NOT EXISTS (
                        SELECT 1
                        FROM "FinancialDimensionSet" AS stored
                        WHERE stored."Key" = fact."DimensionsKey"
                    )
                GROUP BY fact."DimensionsKey";

                CREATE TEMP TABLE "DimensionSetSource" ON COMMIT DROP AS
                SELECT fact."DimensionsKey" AS "Key", fact."Id" AS "FactId"
                FROM "DimensionSetCandidate" AS candidate
                INNER JOIN "FinancialFact" AS fact ON fact."Id" = candidate."FactId"
                WHERE {{StatesItsKey}};

                -- A key whose first fact does not state it in full is taken from any fact that does.
                IF EXISTS (
                    SELECT 1
                    FROM "DimensionSetCandidate" AS candidate
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "DimensionSetSource" AS source WHERE source."Key" = candidate."Key"
                    )
                ) THEN
                    INSERT INTO "DimensionSetSource" ("Key", "FactId")
                    SELECT DISTINCT ON (fact."DimensionsKey") fact."DimensionsKey", fact."Id"
                    FROM "FinancialFact" AS fact
                    WHERE fact."DimensionsKey" IN (
                            SELECT candidate."Key"
                            FROM "DimensionSetCandidate" AS candidate
                            WHERE NOT EXISTS (
                                SELECT 1
                                FROM "DimensionSetSource" AS source
                                WHERE source."Key" = candidate."Key"
                            )
                        )
                        AND {{StatesItsKey}}
                    ORDER BY fact."DimensionsKey", fact."Id";
                END IF;

                INSERT INTO "FinancialDimensionSet" ("Key", "CreationTime")
                SELECT source."Key", now()
                FROM "DimensionSetSource" AS source
                ON CONFLICT DO NOTHING;

                INSERT INTO "FinancialDimensionSetMember" ("DimensionSetKey", "Axis", "Member")
                SELECT source."Key", stated."Axis", stated."Member"
                FROM "DimensionSetSource" AS source
                INNER JOIN "FinancialFactDimension" AS stated ON stated."FinancialFactId" = source."FactId"
                ON CONFLICT DO NOTHING;

                DROP TABLE "DimensionSetSource";
                DROP TABLE "DimensionSetCandidate";
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
