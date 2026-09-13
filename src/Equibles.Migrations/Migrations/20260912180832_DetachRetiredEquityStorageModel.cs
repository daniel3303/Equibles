using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class DetachRetiredEquityStorageModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Model-only cutover: old binaries still need physical source tables during replacement.
        // The verified storage-retirement migration drops them after this native model is deployed.
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the previous binaries without changing preserved source storage.");
}
