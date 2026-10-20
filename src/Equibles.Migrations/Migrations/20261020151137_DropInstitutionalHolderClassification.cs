using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DropInstitutionalHolderClassification : Migration
    {
        // The contract half of UnmapInstitutionalHolderClassification: no running release maps
        // the name-derived column any more, so it goes. Raw SQL, since the model snapshot stopped
        // knowing the column when it was unmapped; its index went with the unmap.
        public const string DropSql = """
            ALTER TABLE "InstitutionalHolder" DROP COLUMN IF EXISTS "Classification";
            """;

        public const string RestoreSql = """
            ALTER TABLE "InstitutionalHolder" ADD COLUMN IF NOT EXISTS "Classification" integer NOT NULL DEFAULT 0;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RestoreSql);
        }
    }
}
