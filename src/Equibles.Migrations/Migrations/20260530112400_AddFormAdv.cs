using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAdv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAdvAdviser",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Crd = table.Column<int>(type: "integer", nullable: false),
                    SecNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    LegalName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    PrimaryBusinessName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    MainOfficeCity = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    MainOfficeState = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    MainOfficeCountry = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    WebsiteAddress = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    SecStatus = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    NumberOfEmployees = table.Column<int>(type: "integer", nullable: true),
                    TotalRegulatoryAum = table.Column<long>(type: "bigint", nullable: true),
                    DiscretionaryAum = table.Column<long>(type: "bigint", nullable: true),
                    NonDiscretionaryAum = table.Column<long>(type: "bigint", nullable: true),
                    ChargesPercentageOfAum = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesHourly = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesSubscription = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesFixed = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesCommissions = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesPerformanceBased = table.Column<bool>(type: "boolean", nullable: false),
                    ChargesOther = table.Column<bool>(type: "boolean", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    UpdateTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAdvAdviser", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviser_Crd",
                table: "FormAdvAdviser",
                column: "Crd",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviser_LegalName",
                table: "FormAdvAdviser",
                column: "LegalName"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormAdvAdviser_TotalRegulatoryAum",
                table: "FormAdvAdviser",
                column: "TotalRegulatoryAum"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormAdvAdviser");
        }
    }
}
