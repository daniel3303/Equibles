using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class FixEntityRegistrationAndNullableIndustry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommonStock_Industry_IndustryId",
                table: "CommonStock");

            migrationBuilder.AddColumn<string>(
                name: "Discriminator",
                table: "File",
                type: "character varying(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "File",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "File",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "IndustryId",
                table: "CommonStock",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "CongressMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressMember", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyShortVolume",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    ShortVolume = table.Column<long>(type: "bigint", nullable: false),
                    ShortExemptVolume = table.Column<long>(type: "bigint", nullable: false),
                    TotalVolume = table.Column<long>(type: "bigint", nullable: false),
                    Market = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyShortVolume", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyShortVolume_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FailToDeliver",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailToDeliver", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FailToDeliver_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsiderOwner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    StateOrCountry = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsDirector = table.Column<bool>(type: "boolean", nullable: false),
                    IsOfficer = table.Column<bool>(type: "boolean", nullable: false),
                    OfficerTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsTenPercentOwner = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderOwner", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShortInterest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettlementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    PreviousShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    ChangeInShortPosition = table.Column<long>(type: "bigint", nullable: false),
                    AverageDailyVolume = table.Column<long>(type: "bigint", nullable: true),
                    DaysToCover = table.Column<decimal>(type: "numeric", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortInterest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortInterest_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CongressionalTrade",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CongressMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssetName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AmountFrom = table.Column<long>(type: "bigint", nullable: false),
                    AmountTo = table.Column<long>(type: "bigint", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CongressionalTrade", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CongressionalTrade_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CongressionalTrade_CongressMember_CongressMemberId",
                        column: x => x.CongressMemberId,
                        principalTable: "CongressMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsiderTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InsiderOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TransactionCode = table.Column<int>(type: "integer", nullable: false),
                    Shares = table.Column<long>(type: "bigint", nullable: false),
                    PricePerShare = table.Column<decimal>(type: "numeric", nullable: false),
                    AcquiredDisposed = table.Column<int>(type: "integer", nullable: false),
                    SharesOwnedAfter = table.Column<long>(type: "bigint", nullable: false),
                    OwnershipNature = table.Column<int>(type: "integer", nullable: false),
                    SecurityTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AccessionNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IsAmendment = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsiderTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsiderTransaction_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InsiderTransaction_InsiderOwner_InsiderOwnerId",
                        column: x => x.InsiderOwnerId,
                        principalTable: "InsiderOwner",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_CongressMemberId_Transacti~",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "CongressMemberId", "TransactionDate", "TransactionType", "AssetName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CommonStockId_TransactionDate",
                table: "CongressionalTrade",
                columns: new[] { "CommonStockId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_CongressMemberId_TransactionDate",
                table: "CongressionalTrade",
                columns: new[] { "CongressMemberId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_FilingDate",
                table: "CongressionalTrade",
                column: "FilingDate");

            migrationBuilder.CreateIndex(
                name: "IX_CongressionalTrade_TransactionDate",
                table: "CongressionalTrade",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_CongressMember_Name",
                table: "CongressMember",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_CommonStockId_Date",
                table: "DailyShortVolume",
                columns: new[] { "CommonStockId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyShortVolume_Date",
                table: "DailyShortVolume",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_CommonStockId_SettlementDate",
                table: "FailToDeliver",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FailToDeliver_SettlementDate",
                table: "FailToDeliver",
                column: "SettlementDate");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderOwner_Name",
                table: "InsiderOwner",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderOwner_OwnerCik",
                table: "InsiderOwner",
                column: "OwnerCik",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_AccessionNumber",
                table: "InsiderTransaction",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_InsiderOwnerId_Transaction~",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "InsiderOwnerId", "TransactionDate", "TransactionCode", "SecurityTitle" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_CommonStockId_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "CommonStockId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_FilingDate",
                table: "InsiderTransaction",
                column: "FilingDate");

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_InsiderOwnerId_TransactionDate",
                table: "InsiderTransaction",
                columns: new[] { "InsiderOwnerId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_TransactionDate",
                table: "InsiderTransaction",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_CommonStockId_SettlementDate",
                table: "ShortInterest",
                columns: new[] { "CommonStockId", "SettlementDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShortInterest_SettlementDate",
                table: "ShortInterest",
                column: "SettlementDate");

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStock_Industry_IndustryId",
                table: "CommonStock",
                column: "IndustryId",
                principalTable: "Industry",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CommonStock_Industry_IndustryId",
                table: "CommonStock");

            migrationBuilder.DropTable(
                name: "CongressionalTrade");

            migrationBuilder.DropTable(
                name: "DailyShortVolume");

            migrationBuilder.DropTable(
                name: "FailToDeliver");

            migrationBuilder.DropTable(
                name: "InsiderTransaction");

            migrationBuilder.DropTable(
                name: "ShortInterest");

            migrationBuilder.DropTable(
                name: "CongressMember");

            migrationBuilder.DropTable(
                name: "InsiderOwner");

            migrationBuilder.DropColumn(
                name: "Discriminator",
                table: "File");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "File");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "File");

            migrationBuilder.AlterColumn<Guid>(
                name: "IndustryId",
                table: "CommonStock",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CommonStock_Industry_IndustryId",
                table: "CommonStock",
                column: "IndustryId",
                principalTable: "Industry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
