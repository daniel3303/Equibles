using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class RegisterSourceIssuerIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "EquityIssuer",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EquityIssuerSourceIdentifier",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquityIssuerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Identifier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityIssuerSourceIdentifier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquityIssuerSourceIdentifier_EquityDirectorySourceRecord_So~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquityIssuerSourceIdentifier_EquityIssuer_EquityIssuerId",
                        column: x => x.EquityIssuerId,
                        principalTable: "EquityIssuer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerSourceIdentifier_EquityIssuerId",
                table: "EquityIssuerSourceIdentifier",
                column: "EquityIssuerId");

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerSourceIdentifier_Source_Identifier",
                table: "EquityIssuerSourceIdentifier",
                columns: new[] { "Source", "Identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquityIssuerSourceIdentifier_SourceRecordId",
                table: "EquityIssuerSourceIdentifier",
                column: "SourceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Source issuer identity and captured evidence must be retained when application binaries roll back.");
        }
    }
}
