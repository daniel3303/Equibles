using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialDimensionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialDimensionSet",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDimensionSet", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FinancialDimensionSetMember",
                columns: table => new
                {
                    DimensionSetKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Axis = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Member = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDimensionSetMember", x => new { x.DimensionSetKey, x.Axis, x.Member });
                    table.ForeignKey(
                        name: "FK_FinancialDimensionSetMember_FinancialDimensionSet_Dimension~",
                        column: x => x.DimensionSetKey,
                        principalTable: "FinancialDimensionSet",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDimensionSetMember_Axis_Member",
                table: "FinancialDimensionSetMember",
                columns: new[] { "Axis", "Member" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialDimensionSetMember");

            migrationBuilder.DropTable(
                name: "FinancialDimensionSet");
        }
    }
}
