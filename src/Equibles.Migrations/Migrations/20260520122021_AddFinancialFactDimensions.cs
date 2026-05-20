using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialFactDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialFactDimension",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinancialFactId = table.Column<Guid>(type: "uuid", nullable: false),
                    Axis = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    Member = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialFactDimension", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialFactDimension_FinancialFact_FinancialFactId",
                        column: x => x.FinancialFactId,
                        principalTable: "FinancialFact",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFactDimension_Axis_Member",
                table: "FinancialFactDimension",
                columns: new[] { "Axis", "Member" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FinancialFactDimension_FinancialFactId_Axis_Member",
                table: "FinancialFactDimension",
                columns: new[] { "FinancialFactId", "Axis", "Member" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FinancialFactDimension");
        }
    }
}
