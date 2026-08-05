using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Per-listing 13F identity: the CommonStockListedCusip table, the nullable
    /// InstitutionalHolding.ListedTicker column, and the holding unique index widened from six
    /// to seven columns (NULLS NOT DISTINCT so the null primary-class rows stay unique).
    ///
    /// The index swap is raw SQL rather than the scaffolded DropIndex/CreateIndex pair, for the
    /// same reasons as WidenInsiderTransactionCoveringIndex: the scaffolded version rebuilds a
    /// multi-GB index under ACCESS EXCLUSIVE held for the whole build (every 13F reader blocks
    /// for minutes on a large install), while CONCURRENTLY (suppressTransaction) plus
    /// create-before-drop keeps the table readable and unique-protected throughout, and
    /// IF NOT EXISTS makes a deployment that pre-built the index by hand a no-op.
    ///
    /// One extra step the precedent didn't need: the old six-column and new seven-column index
    /// truncate to the SAME 63-char name, so the replacement builds under a short explicit name
    /// and is renamed onto the EF name once the old index is gone. The rename is metadata-only.
    ///
    /// Window accepted by design: once the old index drops, a still-running old-code importer's
    /// six-column ON CONFLICT has no matching index and fails with 42P10 until that host rolls
    /// onto the new code; 13F imports retry on their next cycle, so nothing is lost.
    /// </summary>
    public partial class PerListing13FIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent so a hand pre-build (column + index ahead of the deploy) is a no-op.
            migrationBuilder.Sql(
                "ALTER TABLE \"InstitutionalHolding\" ADD COLUMN IF NOT EXISTS \"ListedTicker\" character varying(32);"
            );

            migrationBuilder.CreateTable(
                name: "CommonStockListedCusip",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommonStockListedCusip", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommonStockListedCusip_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockListedCusip_CommonStockId",
                table: "CommonStockListedCusip",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_CommonStockListedCusip_Cusip",
                table: "CommonStockListedCusip",
                column: "Cusip",
                unique: true);

            // A failed CONCURRENTLY build leaves an INVALID index behind, which IF NOT EXISTS
            // would then treat as "exists" forever — drop such a leftover first (an invalid
            // index serves no queries, so the plain drop's brief lock is free), then build.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPerListing' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_HoldingKeyPerListing\"'; END IF; END $$;",
                suppressTransaction: true
            );
            // With ListedTicker still all-null, NULLS NOT DISTINCT groups the new column exactly
            // like the old six-column key, so the build cannot hit a uniqueness violation.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_HoldingKeyPerListing\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"InstitutionalHolderId\", \"ReportDate\", "
                    + "\"ShareType\", \"OptionType\", \"FilingType\", \"ListedTicker\") NULLS NOT DISTINCT;",
                suppressTransaction: true
            );
            // Only now that the replacement is live and valid: drop the six-column original,
            // freeing the truncated EF name the replacement is then renamed onto.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class WHERE relname = 'IX_InstitutionalHolding_HoldingKeyPerListing') THEN "
                    + "EXECUTE 'ALTER INDEX \"IX_InstitutionalHolding_HoldingKeyPerListing\" "
                    + "RENAME TO \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\"'; END IF; END $$;"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonStockListedCusip");

            // Same create-before-drop dance in reverse: rebuild the six-column key under a
            // temporary name, retire the seven-column index, take its name back.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_InstitutionalHolding_HoldingKeyPrimary' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_InstitutionalHolding_HoldingKeyPrimary\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS \"IX_InstitutionalHolding_HoldingKeyPrimary\" "
                    + "ON \"InstitutionalHolding\" (\"CommonStockId\", \"InstitutionalHolderId\", \"ReportDate\", "
                    + "\"ShareType\", \"OptionType\", \"FilingType\") NULLS NOT DISTINCT;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\";",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class WHERE relname = 'IX_InstitutionalHolding_HoldingKeyPrimary') THEN "
                    + "EXECUTE 'ALTER INDEX \"IX_InstitutionalHolding_HoldingKeyPrimary\" "
                    + "RENAME TO \"IX_InstitutionalHolding_CommonStockId_InstitutionalHolderId_Re~\"'; END IF; END $$;"
            );

            migrationBuilder.Sql(
                "ALTER TABLE \"InstitutionalHolding\" DROP COLUMN IF EXISTS \"ListedTicker\";"
            );
        }
    }
}
