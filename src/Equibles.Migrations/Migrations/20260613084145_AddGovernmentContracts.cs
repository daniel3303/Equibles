using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernmentContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GovernmentContract",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardUniqueKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AwardId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AwardType = table.Column<int>(type: "integer", nullable: false),
                    AwardingAgency = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalOutlays = table.Column<decimal>(type: "numeric", nullable: true),
                    ActionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastModifiedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NaicsCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    PscCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernmentContract", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GovernmentContract_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GovernmentContract_ActionDate",
                table: "GovernmentContract",
                column: "ActionDate");

            migrationBuilder.CreateIndex(
                name: "IX_GovernmentContract_AwardUniqueKey",
                table: "GovernmentContract",
                column: "AwardUniqueKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernmentContract_CommonStockId_ActionDate",
                table: "GovernmentContract",
                columns: new[] { "CommonStockId", "ActionDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernmentContract");
        }
    }
}
