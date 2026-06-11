using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressionalAnnualDisclosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CongressionalAnnualDisclosure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CongressMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NetWorthMinimum = table.Column<long>(type: "bigint", nullable: false),
                    NetWorthMaximum = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalAnnualDisclosure", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongressionalAnnualDisclosure_CongressMember_CongressMember~",
                        column: x => x.CongressMemberId,
                        principalTable: "CongressMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CongressionalDisclosureLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CongressionalAnnualDisclosureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RangeMinimum = table.Column<long>(type: "bigint", nullable: false),
                    RangeMaximum = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalDisclosureLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongressionalDisclosureLine_CongressionalAnnualDisclosure_C~",
                        column: x => x.CongressionalAnnualDisclosureId,
                        principalTable: "CongressionalAnnualDisclosure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalAnnualDisclosure_CongressMemberId_Year",
                table: "CongressionalAnnualDisclosure",
                columns: new[] { "CongressMemberId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalAnnualDisclosure_Year",
                table: "CongressionalAnnualDisclosure",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalDisclosureLine_CongressionalAnnualDisclosureId",
                table: "CongressionalDisclosureLine",
                column: "CongressionalAnnualDisclosureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CongressionalDisclosureLine");

            migrationBuilder.DropTable(
                name: "CongressionalAnnualDisclosure");
        }
    }
}
