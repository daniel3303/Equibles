using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Brings pg_search and pgvector up to the versions the ParadeDB image ships. A newer image
    /// loads its libraries against the SQL objects the old version installed, and BM25 queries
    /// fail (missing paradedb functions) until the extensions are updated. The compose image is
    /// pinned to a pg_search that survives cancelled parallel scans, which chunk searches now
    /// use. Updating to the image's default version is a no-op on a fresh database.
    /// </summary>
    public partial class UpdateParadeDbExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '30s';");
            migrationBuilder.Sql("ALTER EXTENSION pg_search UPDATE;");
            migrationBuilder.Sql("ALTER EXTENSION vector UPDATE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // pg_search ships no downgrade scripts, and the image only loads its own version.
        }
    }
}
