using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class TrackCurrentEquityDirectorySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EquityDirectorySnapshotState",
                columns: table => new
                {
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceRecordKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquityDirectorySnapshotState", x => new { x.Source, x.SourceRecordKey });
                    table.ForeignKey(
                        name: "FK_EquityDirectorySnapshotState_EquityDirectorySourceRecord_So~",
                        column: x => x.SourceRecordId,
                        principalTable: "EquityDirectorySourceRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquityDirectorySnapshotState_SourceRecordId",
                table: "EquityDirectorySnapshotState",
                column: "SourceRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Current source checkpoints must survive rollback to prevent stale directory reactivation.");
        }
    }
}
