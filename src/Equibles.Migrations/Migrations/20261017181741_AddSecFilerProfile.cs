using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddSecFilerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecFilerProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Cik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Found = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Sic = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    SicDescription = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StateOfIncorporation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecFilerProfile", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecFilerProfile_Cik",
                table: "SecFilerProfile",
                column: "Cik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecFilerProfile_Sic",
                table: "SecFilerProfile",
                column: "Sic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecFilerProfile");
        }
    }
}
