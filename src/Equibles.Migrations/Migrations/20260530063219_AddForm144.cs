using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddForm144 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Form144Filing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommonStockId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessionNumber = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: true
                    ),
                    FilingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SellerName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    RelationshipToIssuer = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    SecurityClassTitle = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    BrokerName = table.Column<string>(
                        type: "character varying(256)",
                        maxLength: 256,
                        nullable: true
                    ),
                    SharesToBeSold = table.Column<long>(type: "bigint", nullable: false),
                    AggregateMarketValue = table.Column<decimal>(type: "numeric", nullable: false),
                    SharesOutstanding = table.Column<long>(type: "bigint", nullable: false),
                    ApproxSaleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SecuritiesExchangeName = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    Remarks = table.Column<string>(
                        type: "character varying(2048)",
                        maxLength: 2048,
                        nullable: true
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form144Filing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Form144Filing_CommonStock_CommonStockId",
                        column: x => x.CommonStockId,
                        principalTable: "CommonStock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Form144PriorSale",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Form144FilingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerName = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    SecurityClassTitle = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AmountSold = table.Column<long>(type: "bigint", nullable: false),
                    GrossProceeds = table.Column<decimal>(type: "numeric", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Form144PriorSale", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Form144PriorSale_Form144Filing_Form144FilingId",
                        column: x => x.Form144FilingId,
                        principalTable: "Form144Filing",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Form144Filing_AccessionNumber",
                table: "Form144Filing",
                column: "AccessionNumber",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Form144Filing_CommonStockId_FilingDate",
                table: "Form144Filing",
                columns: new[] { "CommonStockId", "FilingDate" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Form144Filing_FilingDate",
                table: "Form144Filing",
                column: "FilingDate"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Form144PriorSale_Form144FilingId",
                table: "Form144PriorSale",
                column: "Form144FilingId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Form144PriorSale");

            migrationBuilder.DropTable(name: "Form144Filing");
        }
    }
}
