using Equibles.Migrations.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.Migrations;

public partial class PrepareCanonicalOwnerBackfill : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        PhysicalEquityOwnerBackfill20260913.Apply(migrationBuilder);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Preserved issuer ownership cannot be reversed.");
}
