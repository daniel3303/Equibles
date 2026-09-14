using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class RetireLegacyFinancialEquityStorage : Migration {
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Infrastructure.EquityStorageRetirementSql.Read("RetireLegacyFinancialEquityStorage20260913.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Retired equity storage requires the verified recovery backup; automatic downgrade cannot recreate original storage.");
}
