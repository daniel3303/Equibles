using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ZeroWithdrawnPositionManagerLegValues : Migration
    {
        // ImpossiblePositionRepairService withdrew a position's value but left its manager legs
        // valued, and the manager breakdown sums the legs, so it kept showing the withdrawn
        // figure. A withdrawn position is never a candidate again, so the fixed writer cannot reach
        // the rows it left. Every other writer that marks a position's value unavailable zeroes its
        // legs too, so any valued leg under a withdrawn position is left over from that defect.
        // No snapshot reads leg values, so nothing needs a rebuild.
        public const string RepairSql = """
            UPDATE "HoldingManagerEntry" AS m
            SET "Value" = 0
            FROM "InstitutionalHolding" AS h
            WHERE m."InstitutionalHoldingId" = h."Id"
              AND h."ValueUnavailable"
              AND h."Value" = 0
              AND m."Value" <> 0;
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
