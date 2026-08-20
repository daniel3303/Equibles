using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddForm144FilerCikBackfillAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FilerCikBackfillAttemptedAt",
                table: "Form144Filing",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FilerCikBackfillAttempts",
                table: "Form144Filing",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilerCikBackfillAttemptedAt",
                table: "Form144Filing");

            migrationBuilder.DropColumn(
                name: "FilerCikBackfillAttempts",
                table: "Form144Filing");
        }
    }
}
