using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class IndexInsiderTransactionSupersededAccessionNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InsiderTransaction_SupersededAccessionNumber",
                table: "InsiderTransaction",
                column: "SupersededAccessionNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InsiderTransaction_SupersededAccessionNumber",
                table: "InsiderTransaction");
        }
    }
}
