using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddHolderProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HolderClassificationEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    MatchedOn = table.Column<int>(type: "integer", nullable: false),
                    Field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HolderClassificationEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HolderClassificationEvidence_InstitutionalHolder_Institutio~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionalHolderProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstitutionalHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    EntityTypeSource = table.Column<int>(type: "integer", nullable: true),
                    EntityTypeSourceKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EntityTypeAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    HasConflictingEvidence = table.Column<bool>(type: "boolean", nullable: false),
                    AdviserCrd = table.Column<int>(type: "integer", nullable: true),
                    AdviserAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    RegulatoryAum = table.Column<long>(type: "bigint", nullable: true),
                    Lei = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ClientBase = table.Column<int>(type: "integer", nullable: false),
                    ClientBaseAumShare = table.Column<decimal>(type: "numeric", nullable: true),
                    PooledVehicleAumShare = table.Column<decimal>(type: "numeric", nullable: true),
                    AdvisesPrivateFunds = table.Column<bool>(type: "boolean", nullable: true),
                    PrivateFundCount = table.Column<int>(type: "integer", nullable: true),
                    PrivateFundFocus = table.Column<int>(type: "integer", nullable: false),
                    PrivateFundGrossAssets = table.Column<long>(type: "bigint", nullable: true),
                    AdvisedSeriesCount = table.Column<int>(type: "integer", nullable: false),
                    IndexSeriesCount = table.Column<int>(type: "integer", nullable: false),
                    EtfSeriesCount = table.Column<int>(type: "integer", nullable: false),
                    AdvisedNetAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    IndexNetAssets = table.Column<decimal>(type: "numeric", nullable: true),
                    AdvisedSeriesAsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    Sic = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DerivationVersion = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionalHolderProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionalHolderProfile_InstitutionalHolder_Institutiona~",
                        column: x => x.InstitutionalHolderId,
                        principalTable: "InstitutionalHolder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HolderClassificationEvidence_InstitutionalHolderId",
                table: "HolderClassificationEvidence",
                column: "InstitutionalHolderId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolderProfile_EntityType",
                table: "InstitutionalHolderProfile",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolderProfile_InstitutionalHolderId",
                table: "InstitutionalHolderProfile",
                column: "InstitutionalHolderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HolderClassificationEvidence");

            migrationBuilder.DropTable(
                name: "InstitutionalHolderProfile");
        }
    }
}
