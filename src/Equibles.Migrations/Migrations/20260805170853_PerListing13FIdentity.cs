using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Additive half of the per-listing 13F identity change: the CommonStockListedCusip table
    /// and the nullable InstitutionalHolding.ListedTicker column. Deliberately transactional
    /// end to end — it either commits whole (with its history row) or rolls back whole, so an
    /// interrupted deploy can always re-run it. The holding unique-index swap lives in the
    /// FOLLOWING migration (PerListing13FIndexSwap) precisely because its CONCURRENTLY
    /// statements cannot be transactional: mixing a plain CREATE TABLE into a batch that
    /// commits before interruptible non-transactional work leaves a half-applied migration
    /// that re-runs into 42P07 and crash-loops the migrator.
    ///
    /// The column add is raw ADD COLUMN IF NOT EXISTS so a hand pre-build ahead of the deploy
    /// is a no-op, and takes a 3s lock_timeout: ALTER TABLE needs ACCESS EXCLUSIVE, and one
    /// long-running 13F read would otherwise park the DDL in the lock queue with every
    /// subsequent reader queued behind it. Failing fast aborts the deploy gate before anything
    /// rolls; the retry is free.
    /// </summary>
    public partial class PerListing13FIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '3s';");
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommonStockListedCusip");

            migrationBuilder.Sql("SET LOCAL lock_timeout = '3s';");
            migrationBuilder.Sql(
                "ALTER TABLE \"InstitutionalHolding\" DROP COLUMN IF EXISTS \"ListedTicker\";"
            );
        }
    }
}
