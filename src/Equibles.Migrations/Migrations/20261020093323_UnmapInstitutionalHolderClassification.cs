using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class UnmapInstitutionalHolderClassification : Migration
    {
        // The model no longer maps InstitutionalHolder.Classification, but the release still
        // running when this migrates does, and EF selects every mapped column when it loads a
        // holder. Dropping the column here would break those containers until the rollout
        // retires them, so only its index goes; the column's NOT NULL DEFAULT 0 fills rows this
        // model inserts. The release after this one drops the column with raw SQL, since the
        // model snapshot no longer knows it.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionalHolder_Classification",
                table: "InstitutionalHolder"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InstitutionalHolder_Classification",
                table: "InstitutionalHolder",
                column: "Classification"
            );
        }
    }
}
