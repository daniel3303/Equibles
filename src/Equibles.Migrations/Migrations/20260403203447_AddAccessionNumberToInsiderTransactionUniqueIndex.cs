using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessionNumberToInsiderTransactionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction");

            migrationBuilder.AddColumn<DateTime>(
                name: "ValueLastRetryAt",
                table: "InstitutionalHolding",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValueRetryCount",
                table: "InstitutionalHolding",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TranscriptCheckStatuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HasTranscripts = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscriptCheckStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TranscriptCheckStatuses_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle", "AccessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranscriptCheckStatuses_CommonStockId",
                table: "TranscriptCheckStatuses",
                column: "CommonStockId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranscriptCheckStatuses");

            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction");

            migrationBuilder.DropColumn(
                name: "ValueLastRetryAt",
                table: "InstitutionalHolding");

            migrationBuilder.DropColumn(
                name: "ValueRetryCount",
                table: "InstitutionalHolding");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle" },
                unique: true);
        }
    }
}
