using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderTransactionReportedPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsPriceValid",
                table: "InsiderTransaction",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true
            );

            // Reset previously-flagged rows to "not evaluated yet" (null) so
            // the maintenance recompute re-checks and repairs them under the
            // new logic. Rows that were valid stay valid.
            migrationBuilder.Sql(
                "UPDATE \"InsiderTransaction\" SET \"IsPriceValid\" = NULL WHERE \"IsPriceValid\" = false;"
            );

            migrationBuilder.AddColumn<decimal>(
                name: "ReportedPricePerShare",
                table: "InsiderTransaction",
                type: "numeric",
                nullable: false,
                defaultValue: 0m
            );

            // Backfill the as-filed price for every existing row. No repairs
            // have been applied yet, so the original equals the current
            // PricePerShare; this keeps ReportedPricePerShare non-null.
            migrationBuilder.Sql(
                "UPDATE \"InsiderTransaction\" SET \"ReportedPricePerShare\" = \"PricePerShare\";"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReportedPricePerShare", table: "InsiderTransaction");

            // Collapse the tri-state back to a non-null bool: pending (null)
            // rows revert to the original default (true) before the column is
            // made NOT NULL again.
            migrationBuilder.Sql(
                "UPDATE \"InsiderTransaction\" SET \"IsPriceValid\" = true WHERE \"IsPriceValid\" IS NULL;"
            );

            migrationBuilder.AlterColumn<bool>(
                name: "IsPriceValid",
                table: "InsiderTransaction",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true
            );
        }
    }
}
