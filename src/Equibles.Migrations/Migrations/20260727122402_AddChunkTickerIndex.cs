using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Btree on Chunk(Ticker) for the hybrid searcher's ticker-scoped vector arm (Chunk by
    /// ticker → Embedding by ChunkId). Raw SQL with CREATE INDEX CONCURRENTLY
    /// (suppressTransaction) so the build takes no write lock on the live Chunk table — the
    /// chunker writes continuously — and IF NOT EXISTS keeps it idempotent for deployments
    /// that pre-built the index by hand.
    /// </summary>
    public partial class AddChunkTickerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A failed CONCURRENTLY build leaves an INVALID index behind, which IF NOT EXISTS
            // would then treat as "exists" forever — drop such a leftover first (an invalid
            // index serves no queries, so the plain drop's brief lock is free), then build.
            migrationBuilder.Sql(
                "DO $$ BEGIN "
                    + "IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_index i ON i.indexrelid = c.oid "
                    + "WHERE c.relname = 'IX_Chunk_Ticker' AND NOT i.indisvalid) THEN "
                    + "EXECUTE 'DROP INDEX \"IX_Chunk_Ticker\"'; END IF; END $$;",
                suppressTransaction: true
            );
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Chunk_Ticker\" "
                    + "ON \"Chunk\" (\"Ticker\");",
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Chunk_Ticker\";",
                suppressTransaction: true
            );
        }
    }
}
