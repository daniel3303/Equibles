using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class TrackFinancialFactsCalendarEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarEvidenceFingerprint",
                table: "FinancialFactsSyncStatus",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "XbrlCalendarEvidenceFingerprint",
                table: "Document",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarEvidenceFingerprint",
                table: "FinancialFactsSyncStatus");

            migrationBuilder.DropColumn(
                name: "XbrlCalendarEvidenceFingerprint",
                table: "Document");
        }
    }
}
