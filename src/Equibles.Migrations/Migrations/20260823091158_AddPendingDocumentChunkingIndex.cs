using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Adds the partial FIFO index used by the pending-document chunk queue. The build is
    /// restart-safe because PostgreSQL can retain an invalid index after an interrupted concurrent
    /// build, and a crash can occur after a successful build but before EF records the migration.
    /// </summary>
    public partial class AddPendingDocumentChunkingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_Document_PendingChunking' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_Document_PendingChunking\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Document_PendingChunking\" "
                    + "ON \"Document\" (\"CreationTime\", \"Id\") WHERE \"ChunkedAt\" IS NULL;",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Document_PendingChunking\";",
                suppressTransaction: true
            );
        }
    }
}
