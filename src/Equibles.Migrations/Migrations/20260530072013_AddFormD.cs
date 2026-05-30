using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFormD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormDFiling",
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
                    EntityName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    EntityType = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    JurisdictionOfInc = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    YearOfIncorporation = table.Column<int>(type: "integer", nullable: true),
                    IndustryGroup = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    FederalExemptions = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    DateOfFirstSale = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalOfferingAmount = table.Column<long>(type: "bigint", nullable: true),
                    IsOfferingAmountIndefinite = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    TotalAmountSold = table.Column<long>(type: "bigint", nullable: false),
                    TotalRemaining = table.Column<long>(type: "bigint", nullable: true),
                    IsRemainingIndefinite = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumInvestmentAccepted = table.Column<long>(type: "bigint", nullable: false),
                    HasNonAccreditedInvestors = table.Column<bool>(
                        type: "boolean",
                        nullable: false
                    ),
                    TotalNumberAlreadyInvested = table.Column<int>(
                        type: "integer",
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDFiling", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormDFiling_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "FormDRelatedPerson",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FormDFilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    Relationships = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDRelatedPerson", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormDRelatedPerson_FormDFiling_FormDFilingId",
                        column: x => x.FormDFilingId,
                        principalTable: "FormDFiling",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormDFiling_AccessionNumber",
                table: "FormDFiling",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormDFiling_CommonStockId_FilingDate",
                table: "FormDFiling",
                columns: new[] { "CommonStockId", "FilingDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormDFiling_FilingDate",
                table: "FormDFiling",
                column: "FilingDate"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FormDRelatedPerson_FormDFilingId",
                table: "FormDRelatedPerson",
                column: "FormDFilingId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormDRelatedPerson");

            migrationBuilder.DropTable(name: "FormDFiling");
        }
    }
}
