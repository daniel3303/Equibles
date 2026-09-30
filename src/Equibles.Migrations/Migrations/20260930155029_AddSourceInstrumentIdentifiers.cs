using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceInstrumentIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityListingSourceIdentifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityListingSourceIdentifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquityListingSourceIdentifier_EquityDirectorySourceRecord_S~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquityListingSourceIdentifier_EquityListing_EquityListingId",
                        column: x => x.EquityListingId,
                        principalTable: "EquityListing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquitySecuritySourceIdentifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquitySecurityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquitySecuritySourceIdentifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquitySecuritySourceIdentifier_EquityDirectorySourceRecord_~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquitySecuritySourceIdentifier_EquitySecurity_EquitySecurit~",
                        column: x => x.EquitySecurityId,
                        principalTable: "EquitySecurity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityListingSourceIdentifier_EquityListingId",
                table: "EquityListingSourceIdentifier",
                column: "EquityListingId");

            migrationBuilder.CreateIndex(
                name: "IX_EquityListingSourceIdentifier_Source_Identifier",
                table: "EquityListingSourceIdentifier",
                columns: new[] { "Source", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquityListingSourceIdentifier_SourceRecordId",
                table: "EquityListingSourceIdentifier",
                column: "SourceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySecuritySourceIdentifier_EquitySecurityId",
                table: "EquitySecuritySourceIdentifier",
                column: "EquitySecurityId");

            migrationBuilder.CreateIndex(
                name: "IX_EquitySecuritySourceIdentifier_Source_Identifier",
                table: "EquitySecuritySourceIdentifier",
                columns: new[] { "Source", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquitySecuritySourceIdentifier_SourceRecordId",
                table: "EquitySecuritySourceIdentifier",
                column: "SourceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquityListingSourceIdentifier");

            migrationBuilder.DropTable(
                name: "EquitySecuritySourceIdentifier");
        }
    }
}
