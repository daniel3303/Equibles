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
