using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddBeneficialOwnershipDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BeneficialOwnershipFiling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FilingType = table.Column<int>(type: "integer", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DateOfEvent = table.Column<DateOnly>(type: "date", nullable: true),
                    FilerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IssuerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    IssuerCusip = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Rules = table.Column<int>(type: "integer", nullable: false),
                    FilingPersonTypes = table.Column<int>(type: "integer", nullable: false),
                    UnrecognizedFilingPersonTypeCodes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsUnreadable = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficialOwnershipFiling", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BeneficialOwnershipReportingPerson",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BeneficialOwnershipFilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Types = table.Column<int>(type: "integer", nullable: false),
                    UnrecognizedTypeCodes = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CitizenshipOrOrganization = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SoleVotingPower = table.Column<long>(type: "bigint", nullable: false),
                    SharedVotingPower = table.Column<long>(type: "bigint", nullable: false),
                    SoleDispositivePower = table.Column<long>(type: "bigint", nullable: false),
                    SharedDispositivePower = table.Column<long>(type: "bigint", nullable: false),
                    AggregateAmountOwned = table.Column<long>(type: "bigint", nullable: false),
                    PercentOfClass = table.Column<decimal>(type: "numeric(7,4)", nullable: true),
                    IsLead = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficialOwnershipReportingPerson", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BeneficialOwnershipReportingPerson_BeneficialOwnershipFilin~",
                        column: x => x.BeneficialOwnershipFilingId,
                        principalTable: "BeneficialOwnershipFiling",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BeneficialOwnershipFiling_AccessionNumber",
                table: "BeneficialOwnershipFiling",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeneficialOwnershipFiling_FilerCik",
                table: "BeneficialOwnershipFiling",
                column: "FilerCik");

            migrationBuilder.CreateIndex(
                name: "IX_BeneficialOwnershipFiling_IssuerCik",
                table: "BeneficialOwnershipFiling",
                column: "IssuerCik");

            migrationBuilder.CreateIndex(
                name: "IX_BeneficialOwnershipReportingPerson_BeneficialOwnershipFilin~",
                table: "BeneficialOwnershipReportingPerson",
                columns: new[] { "BeneficialOwnershipFilingId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeneficialOwnershipReportingPerson_Cik",
                table: "BeneficialOwnershipReportingPerson",
                column: "Cik");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BeneficialOwnershipReportingPerson");

            migrationBuilder.DropTable(
                name: "BeneficialOwnershipFiling");
        }
    }
}
