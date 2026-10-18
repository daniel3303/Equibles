using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ClearDebtSecurityShareFigures : Migration
    {
        // A listed note (RegistrationType 3, DebtSecurities) has no share count or market cap;
        // these rows carried the issuer's common figures.
        public const string RepairSql = """
            UPDATE "EquitySecurity"
            SET "SharesOutstanding" = 0, "MarketCapitalization" = 0
            WHERE "RegistrationType" = 3
              AND ("SharesOutstanding" <> 0 OR "MarketCapitalization" <> 0);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RepairSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
