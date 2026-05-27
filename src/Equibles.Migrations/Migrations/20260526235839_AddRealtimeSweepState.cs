using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddRealtimeSweepState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RealtimeSweepState",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerName = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    SweptThrough = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RealtimeSweepState", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RealtimeSweepState_WorkerName",
                table: "RealtimeSweepState",
                column: "WorkerName",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RealtimeSweepState");
        }
    }
}
