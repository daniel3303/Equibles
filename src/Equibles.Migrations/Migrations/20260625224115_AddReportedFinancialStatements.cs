using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddReportedFinancialStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReportedStatementsCaptureAttempts",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportedStatementsContentId",
                table: "Document",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedStatementsParseAttempts",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReportedStatementsParseVersion",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReportedStatementsStatus",
                table: "Document",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "ReportedStatementsUncompressedSize",
                table: "Document",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportedFinancialStatement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RoleUri = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RoleShortName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ReportFileName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsParenthetical = table.Column<bool>(type: "boolean", nullable: false),
                    FiscalYear = table.Column<int>(type: "integer", nullable: false),
                    FiscalPeriod = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Form = table.Column<string>(type: "text", nullable: false),
                    FiledDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Scale = table.Column<long>(type: "bigint", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportedFinancialStatement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportedFinancialStatement_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportedFinancialStatement_Document_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Document",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Document_ReportedStatementsContentId",
                table: "Document",
                column: "ReportedStatementsContentId");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ReportedStatementsStatus",
                table: "Document",
                column: "ReportedStatementsStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Document_ReportedStatementsStatus_ReportedStatementsParseVe~",
                table: "Document",
                columns: new[] { "ReportedStatementsStatus", "ReportedStatementsParseVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportedFinancialStatement_CommonStockId_Kind_FiscalYear_Fi~",
                table: "ReportedFinancialStatement",
                columns: new[] { "CommonStockId", "Kind", "FiscalYear", "FiscalPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportedFinancialStatement_DocumentId",
                table: "ReportedFinancialStatement",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportedFinancialStatement_DocumentId_RoleUri",
                table: "ReportedFinancialStatement",
                columns: new[] { "DocumentId", "RoleUri" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Document_File_ReportedStatementsContentId",
                table: "Document",
                column: "ReportedStatementsContentId",
                principalTable: "File",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Document_File_ReportedStatementsContentId",
                table: "Document");

            migrationBuilder.DropTable(
                name: "ReportedFinancialStatement");

            migrationBuilder.DropIndex(
                name: "IX_Document_ReportedStatementsContentId",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_ReportedStatementsStatus",
                table: "Document");

            migrationBuilder.DropIndex(
                name: "IX_Document_ReportedStatementsStatus_ReportedStatementsParseVe~",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsCaptureAttempts",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsContentId",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsParseAttempts",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsParseVersion",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsStatus",
                table: "Document");

            migrationBuilder.DropColumn(
                name: "ReportedStatementsUncompressedSize",
                table: "Document");
        }
    }
}
