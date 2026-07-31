using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedManagerAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FilingOtherManager_AccessionNumber",
                table: "FilingOtherManager");

            migrationBuilder.AddColumn<string>(
                name: "SharedManagerNumbers",
                table: "HoldingManagerEntry",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilingOtherManager_AccessionNumber_Direction_SequenceNumber",
                table: "FilingOtherManager",
                columns: new[] { "AccessionNumber", "Direction", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FilingOtherManager_AccessionNumber_Direction_SequenceNumber",
                table: "FilingOtherManager");

            migrationBuilder.DropColumn(
                name: "SharedManagerNumbers",
                table: "HoldingManagerEntry");

            migrationBuilder.CreateIndex(
                name: "IX_FilingOtherManager_AccessionNumber",
                table: "FilingOtherManager",
                column: "AccessionNumber");
        }
    }
}
