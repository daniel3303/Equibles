using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DropFinancialFactDimension : Migration
    {
        // The contract half of UnmapFinancialFactDimension: no running release reads or writes
        // the per-fact dimension rows any more (each combination lives once in
        // FinancialDimensionSet), so the table goes. Raw SQL, since the model snapshot stopped
        // knowing it when it was unmapped.
        //
        // Dropping it also takes ACCESS EXCLUSIVE on FinancialFact, where its foreign key's
        // triggers live. Both locks are taken first under a 3 s lock_timeout, retried for up to
        // twenty minutes: waiting unbounded behind one long fact read would queue every other
        // fact query behind the drop. A failed attempt holds nothing.
        public const string DropSql = """
            DO $$
            DECLARE attempt int := 0;
            BEGIN
                IF to_regclass('"FinancialFactDimension"') IS NULL THEN
                    RETURN;
                END IF;
                LOOP
                    attempt := attempt + 1;
                    BEGIN
                        SET LOCAL lock_timeout = '3s';
                        LOCK TABLE "FinancialFact", "FinancialFactDimension" IN ACCESS EXCLUSIVE MODE;
                        SET LOCAL lock_timeout TO DEFAULT;
                        EXIT;
                    EXCEPTION WHEN lock_not_available OR deadlock_detected THEN
                        IF attempt >= 200 THEN
                            RAISE;
                        END IF;
                        PERFORM pg_sleep(3);
                    END;
                END LOOP;
                DROP TABLE "FinancialFactDimension";
            END $$;
            """;

        // The empty table as the model last mapped it; the rows are not restored.
        public const string RestoreSql = """
            CREATE TABLE IF NOT EXISTS "FinancialFactDimension" (
                "Id" uuid NOT NULL,
                "FinancialFactId" uuid NOT NULL,
                "Axis" character varying(256) NOT NULL,
                "Member" character varying(256) NOT NULL,
                "CreationTime" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_FinancialFactDimension" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_FinancialFactDimension_FinancialFact_FinancialFactId"
                    FOREIGN KEY ("FinancialFactId") REFERENCES "FinancialFact" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_FinancialFactDimension_Axis_Member"
                ON "FinancialFactDimension" ("Axis", "Member");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FinancialFactDimension_FinancialFactId_Axis_Member"
                ON "FinancialFactDimension" ("FinancialFactId", "Axis", "Member");
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RestoreSql);
        }
    }
}
