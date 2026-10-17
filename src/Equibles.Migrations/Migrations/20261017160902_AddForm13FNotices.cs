using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddForm13FNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Form13FNotice",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FilerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    FilerName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    Form13FFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CrdNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SecFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DataSetFileName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form13FNotice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Form13FNoticeDataSet",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NoticeCount = table.Column<int>(type: "integer", nullable: false),
                    ParserVersion = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form13FNoticeDataSet", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Form13FNoticeReporter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Form13FNoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Form13FFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CrdNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SecFileNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form13FNoticeReporter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Form13FNoticeReporter_Form13FNotice_Form13FNoticeId",
                        column: x => x.Form13FNoticeId,
                        principalTable: "Form13FNotice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNotice_AccessionNumber",
                table: "Form13FNotice",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNotice_FilerCik_ReportDate",
                table: "Form13FNotice",
                columns: new[] { "FilerCik", "ReportDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNoticeDataSet_FileName",
                table: "Form13FNoticeDataSet",
                column: "FileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNoticeReporter_Cik",
                table: "Form13FNoticeReporter",
                column: "Cik");

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNoticeReporter_Form13FFileNumber",
                table: "Form13FNoticeReporter",
                column: "Form13FFileNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Form13FNoticeReporter_Form13FNoticeId_Ordinal",
                table: "Form13FNoticeReporter",
                columns: new[] { "Form13FNoticeId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Form13FNoticeDataSet");

            migrationBuilder.DropTable(
                name: "Form13FNoticeReporter");

            migrationBuilder.DropTable(
                name: "Form13FNotice");
        }
    }
}
