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
        // the rows it left. Legs sum to their position's value at import, revaluation and merge,
        // and every other writer that marks a value unavailable either zeroes the legs or acts on
        // a position whose legs are already zero, so a valued leg under a position at value 0 is
        // left over from that defect. A merged position can be unavailable with valued legs, but
        // it carries a value, which the value-0 condition keeps out.
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
