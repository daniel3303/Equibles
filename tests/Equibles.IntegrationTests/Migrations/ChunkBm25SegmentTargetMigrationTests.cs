using Equibles.IntegrationTests.Helpers;
using Npgsql;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

/// <summary>
/// The Chunk BM25 index only keeps merging past its 1GB layer while its segment target stays
/// low, and the model cannot express that option: a migration that recreates the index from the
/// attribute alone would silently bring back unbounded segment growth.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkBm25SegmentTargetMigrationTests(ParadeDbFixture fixture)
{
    [Fact]
    public async Task ChunkBm25Index_TargetsThreeSegmentsAndKeepsItsFieldOptions()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT reloptions FROM pg_class "
                + "WHERE relname = 'IX_Chunk_Id_Content_DocumentType_DocumentId_Ticker_ReportingDa~'",
            connection
        );

        var options = (string[])await command.ExecuteScalarAsync();

        Assert.Contains("target_segment_count=3", options);
        Assert.Contains("key_field=Id", options);
        Assert.Contains(
            options,
            option => option.StartsWith("text_fields=", StringComparison.Ordinal)
        );
    }
}
