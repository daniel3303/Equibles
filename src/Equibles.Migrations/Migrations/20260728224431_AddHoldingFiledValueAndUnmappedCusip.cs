using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldingFiledValueAndUnmappedCusip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FiledValue",
                table: "InstitutionalHolding",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnmappedCusip",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cusip = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Positions = table.Column<int>(type: "integer", nullable: false),
                    FiledValue = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnmappedCusip", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnmappedCusip_Cusip_ReportDate",
                table: "UnmappedCusip",
                columns: new[] { "Cusip", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnmappedCusip_FiledValue",
                table: "UnmappedCusip",
                column: "FiledValue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UnmappedCusip");

            migrationBuilder.DropColumn(
                name: "FiledValue",
                table: "InstitutionalHolding");
        }
    }
}
