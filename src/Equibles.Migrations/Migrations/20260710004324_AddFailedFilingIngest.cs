using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddFailedFilingIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FailedFilingIngest",
                columns: table => new
                {
                    AccessionNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Cik = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FormType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedFilingIngest", x => x.AccessionNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailedFilingIngest_NextRetryAt",
                table: "FailedFilingIngest",
                column: "NextRetryAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FailedFilingIngest");
        }
    }
}
