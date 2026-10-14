using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddEquityIssuerMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityIssuerMerge",
                columns: table => new
                {
                    RetiredIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SurvivingIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceSourceRecordIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    RetiredIssuerJson = table.Column<string>(type: "jsonb", nullable: false),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: false),
                    MergedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerReferencesRepointedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityIssuerMerge", x => x.RetiredIssuerId);
                    table.ForeignKey(
                        name: "FK_EquityIssuerMerge_EquityIssuer_SurvivingIssuerId",
                        column: x => x.SurvivingIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IsinLegalEntityLookup",
                columns: table => new
                {
                    Isin = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    LegalEntityIdentifier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RelatedIsins = table.Column<List<string>>(type: "text[]", nullable: false),
                    RelatedIsinCount = table.Column<int>(type: "integer", nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IsinLegalEntityLookup", x => x.Isin);
                    table.ForeignKey(
                        name: "FK_IsinLegalEntityLookup_EquityDirectorySourceRecord_SourceRec~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerMerge_CustomerReferencesRepointedAt",
                table: "EquityIssuerMerge",
                column: "CustomerReferencesRepointedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerMerge_SurvivingIssuerId",
                table: "EquityIssuerMerge",
                column: "SurvivingIssuerId");

            migrationBuilder.CreateIndex(
                name: "IX_IsinLegalEntityLookup_LegalEntityIdentifier",
                table: "IsinLegalEntityLookup",
                column: "LegalEntityIdentifier");

            migrationBuilder.CreateIndex(
                name: "IX_IsinLegalEntityLookup_NextAttemptAt",
                table: "IsinLegalEntityLookup",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_IsinLegalEntityLookup_SourceRecordId",
                table: "IsinLegalEntityLookup",
                column: "SourceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquityIssuerMerge");

            migrationBuilder.DropTable(
                name: "IsinLegalEntityLookup");
        }
    }
}
