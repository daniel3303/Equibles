using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFdaCatalysts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FdaCatalyst",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalystType = table.Column<int>(type: "integer", nullable: false),
                    MeetingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Committee = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    PublicationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FdaCatalyst", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FdaCatalyst_CatalystType_MeetingDate",
                table: "FdaCatalyst",
                columns: new[] { "CatalystType", "MeetingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FdaCatalyst_CommonStockId",
                table: "FdaCatalyst",
                column: "CommonStockId");

            migrationBuilder.CreateIndex(
                name: "IX_FdaCatalyst_MeetingDate",
                table: "FdaCatalyst",
                column: "MeetingDate");

            migrationBuilder.CreateIndex(
                name: "IX_FdaCatalyst_SourceReference",
                table: "FdaCatalyst",
                column: "SourceReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FdaCatalyst");
        }
    }
}
