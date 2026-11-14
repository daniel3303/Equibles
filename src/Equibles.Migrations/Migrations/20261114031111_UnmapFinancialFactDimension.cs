using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class UnmapFinancialFactDimension : Migration
    {
        // The model no longer maps FinancialFactDimension, but the release still running when
        // this migrates inserts into it for every dimensional fact it stores. Dropping the table
        // here would fail those inserts until the rollout retires them, so the table stays, with
        // the foreign key whose cascade removes a deleted fact's rows. The release after this one
        // drops it with raw SQL, since the model snapshot no longer knows it.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) { }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) { }
    }
}
