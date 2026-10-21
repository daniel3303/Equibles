using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Equibles.Migrations.Migrations
{
    /// <summary>
    /// Lets the Chunk BM25 index compact again. pg_search drops every background merge layer
    /// larger than two thirds of (index size / target_segment_count), and the target defaults to
    /// the CPU count, so a large index stops merging above its 1GB layer: segments past that
    /// size never merge again, keep every deleted chunk, and each search pays a fixed read per
    /// segment. Three segments keep the 10GB layer while live segments total over about 47GB,
    /// which a large index stays above once its deleted chunks are merged away. Searches scan
    /// on the leader alone, so fewer segments costs no parallelism.
    ///
    /// Reloption only, so the background merger does the compaction on its own schedule; the
    /// short lock_timeout lets a busy index fail the deploy instead of queueing searches behind
    /// ACCESS EXCLUSIVE. The Bm25Index attribute cannot carry the option, so a migration that
    /// recreates this index must set it again (pinned by ChunkBm25SegmentTargetMigrationTests).
    /// </summary>
    public partial class CompactChunkBm25Segments : Migration
    {
        private const string IndexName =
            "\"IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~\"";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql($"ALTER INDEX {IndexName} SET (target_segment_count = 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
            migrationBuilder.Sql($"ALTER INDEX {IndexName} RESET (target_segment_count);");
        }
    }
}
