using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernmentContractRecipientParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MatchingVersion",
                table: "GovernmentContractsScanState",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GovernmentContractRecipientParent",
                columns: table => new
                {
                    RecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ParentRecipientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParentName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernmentContractRecipientParent", x => x.RecipientId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GovernmentContractRecipientParent");

            migrationBuilder.DropColumn(
                name: "MatchingVersion",
                table: "GovernmentContractsScanState");
        }
    }
}
