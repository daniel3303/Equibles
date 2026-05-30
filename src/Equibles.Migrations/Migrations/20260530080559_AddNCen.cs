using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNCen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NCenFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrantName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    InvestmentCompanyType = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    InvestmentCompanyFileNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    RegistrantLei = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    State = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    Country = table.Column<string>(
                        type: "character varying(8)",
                        maxLength: 8,
                        nullable: true
                    ),
                    ReportEndingPeriod = table.Column<DateOnly>(type: "date", nullable: false),
                    IsReportPeriodLessThan12Months = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    IsFirstFiling = table.Column<bool>(type: "boolean", nullable: false),
                    IsLastFiling = table.Column<bool>(type: "boolean", nullable: false),
                    IsFamilyInvestmentCompany = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenFiling_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "NCenServiceProvider",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NCenFilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    Country = table.Column<string>(
                        type: "character varying(8)",
                        maxLength: 8,
                        nullable: true
                    ),
                    IsAffiliated = table.Column<bool>(type: "boolean", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NCenServiceProvider", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NCenServiceProvider_NCenFiling_NCenFilingId",
                        column: x => x.NCenFilingId,
                        principalTable: "NCenFiling",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NCenFiling_AccessionNumber",
                table: "NCenFiling",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_NCenFiling_CommonStockId_FilingDate",
                table: "NCenFiling",
                columns: new[] { "CommonStockId", "FilingDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NCenFiling_FilingDate",
                table: "NCenFiling",
                column: "FilingDate"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NCenServiceProvider_NCenFilingId",
                table: "NCenServiceProvider",
                column: "NCenFilingId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NCenServiceProvider");

            migrationBuilder.DropTable(name: "NCenFiling");
        }
    }
}
