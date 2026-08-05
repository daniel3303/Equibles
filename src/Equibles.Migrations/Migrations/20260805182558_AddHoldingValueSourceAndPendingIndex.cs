using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the value-provenance column and the pending-pair index behind the 13F filed-value
    /// fallback.
    ///
    /// The index is built with raw SQL rather than the scaffolded CreateIndex: CONCURRENTLY
    /// (suppressTransaction) because InstitutionalHolding holds tens of millions of rows and a
    /// plain build takes ACCESS EXCLUSIVE for the whole scan — inside the deploy's migrate gate
    /// that both stalls the release and locks out every holdings read — and IF NOT EXISTS so a
    /// pre-built index is a no-op. The AddColumn with a default is metadata-only on this
    /// PostgreSQL version and stays scaffolded.
    /// </summary>
    public partial class AddHoldingValueSourceAndPendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValueSource",
                table: "InstitutionalHolding",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // A failed CONCURRENTLY build leaves an INVALID index behind, which IF NOT EXISTS
            // would then treat as "exists" forever — drop such a leftover first (an invalid
            // index serves no queries, so the plain drop's brief lock is free), then build.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_ValuePending_Pairs' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_ValuePending_Pairs\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_ValuePending_Pairs\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"ListedTicker\", \"ReportDate\") "
                    + "WHERE \"ValuePending\";",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolding_ValuePending_Pairs",
                table: "InstitutionalHolding");

            migrationBuilder.DropColumn(
                name: "ValueSource",
                table: "InstitutionalHolding");
        }
    }
}
