using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressionalTradeAmendment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CongressionalTradeAmendment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingKind = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceRowIndex = table.Column<int>(type: "integer", nullable: false),
                    CongressMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FiledTicker = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Subholding = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AmountFrom = table.Column<long>(type: "bigint", nullable: false),
                    AmountTo = table.Column<long>(type: "bigint", nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    SupersededSourceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SupersededSourceRowIndex = table.Column<int>(type: "integer", nullable: true),
                    FirstDisclosedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalTradeAmendment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongressionalTradeAmendment_CongressMember_CongressMemberId",
                        column: x => x.CongressMemberId,
                        principalTable: "CongressMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTradeAmendment_CongressMemberId",
                table: "CongressionalTradeAmendment",
                column: "CongressMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTradeAmendment_SourceRow",
                table: "CongressionalTradeAmendment",
                columns: new[] { "FilingKind", "SourceId", "SourceRowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTradeAmendment_Superseded",
                table: "CongressionalTradeAmendment",
                columns: new[] { "SupersededSourceId", "SupersededSourceRowIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTradeAmendment_Verdict",
                table: "CongressionalTradeAmendment",
                column: "Verdict");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CongressionalTradeAmendment");
        }
    }
}
