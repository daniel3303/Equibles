using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddCongressLegislatorTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BioguideIdSource",
                table: "CongressMember",
                type: "integer",
                nullable: true);

            // Every id stored before this migration came from the reviewed catalog.
            migrationBuilder.Sql(
                "UPDATE \"CongressMember\" SET \"BioguideIdSource\" = 0 WHERE \"BioguideId\" IS NOT NULL;");

            migrationBuilder.CreateTable(
                name: "CongressLegislator",
                columns: table => new
                {
                    BioguideId = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Suffix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    OfficialFullName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressLegislator", x => x.BioguideId);
                });

            migrationBuilder.CreateTable(
                name: "CongressMemberTerm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BioguideId = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Chamber = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character(2)", nullable: false),
                    District = table.Column<short>(type: "smallint", nullable: true),
                    Party = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Caucus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressMemberTerm", x => x.Id);
                    table.CheckConstraint("CK_CongressMemberTerm_EndAfterStart", "\"EndDate\" > \"StartDate\"");
                    table.ForeignKey(
                        name: "FK_CongressMemberTerm_CongressLegislator_BioguideId",
                        column: x => x.BioguideId,
                        principalTable: "CongressLegislator",
                        principalColumn: "BioguideId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressMemberTerm_BioguideId_Chamber_StartDate",
                table: "CongressMemberTerm",
                columns: new[] { "BioguideId", "Chamber", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressMemberTerm_Chamber_State_District_StartDate",
                table: "CongressMemberTerm",
                columns: new[] { "Chamber", "State", "District", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CongressMemberTerm");

            migrationBuilder.DropTable(
                name: "CongressLegislator");

            migrationBuilder.DropColumn(
                name: "BioguideIdSource",
                table: "CongressMember");
        }
    }
}
