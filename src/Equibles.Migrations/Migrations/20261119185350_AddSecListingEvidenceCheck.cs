using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSecListingEvidenceCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecListingEvidenceCheck",
                columns: table => new
                {
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tickers = table.Column<List<string>>(type: "text[]", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    RegistrationAccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CertificationAccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProspectusAccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProspectusFiledOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecListingEvidenceCheck", x => x.Cik);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecListingEvidenceCheck");
        }
    }
}
