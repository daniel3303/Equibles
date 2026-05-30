using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddNport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NportFiling",
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
                    SeriesName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    SeriesId = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    SeriesLei = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    ReportPeriodDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReportPeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalAssets = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalLiabilities = table.Column<decimal>(type: "numeric", nullable: false),
                    NetAssets = table.Column<decimal>(type: "numeric", nullable: false),
                    IsFinalFiling = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NportFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NportFiling_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "NportHolding",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NportFilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    Title = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    Cusip = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    Isin = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    Lei = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    Balance = table.Column<decimal>(type: "numeric", nullable: false),
                    Units = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    Currency = table.Column<string>(
                        type: "character varying(8)",
                        maxLength: 8,
                        nullable: true
                    ),
                    ValueUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentValue = table.Column<decimal>(type: "numeric", nullable: false),
                    PayoffProfile = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    AssetCategory = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    IssuerCategory = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    InvestmentCountry = table.Column<string>(
                        type: "character varying(8)",
                        maxLength: 8,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NportHolding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NportHolding_NportFiling_NportFilingId",
                        column: x => x.NportFilingId,
                        principalTable: "NportFiling",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NportFiling_AccessionNumber",
                table: "NportFiling",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_NportFiling_CommonStockId_FilingDate",
                table: "NportFiling",
                columns: new[] { "CommonStockId", "FilingDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_NportFiling_FilingDate",
                table: "NportFiling",
                column: "FilingDate"
            );

            migrationBuilder.CreateIndex(
                name: "IX_NportHolding_NportFilingId",
                table: "NportHolding",
                column: "NportFilingId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NportHolding");

            migrationBuilder.DropTable(name: "NportFiling");
        }
    }
}
