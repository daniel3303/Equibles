using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddForm144FilerCikAndPlanAdoptionDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilerCik",
                table: "Form144Filing",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlanAdoptionDate",
                table: "Form144Filing",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Form144Filing_FilerCik_FilingDate",
                table: "Form144Filing",
                columns: new[] { "FilerCik", "FilingDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Form144Filing_FilerCik_FilingDate",
                table: "Form144Filing");

            migrationBuilder.DropColumn(
                name: "FilerCik",
                table: "Form144Filing");

            migrationBuilder.DropColumn(
                name: "PlanAdoptionDate",
                table: "Form144Filing");
        }
    }
}
