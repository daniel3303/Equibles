using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.Sec.Repositories;

// The last ranked-prefix round ChunkRankedScope validated against the parent scope, kept outside
// the scan so a timeout raised mid-round can still return it.
internal sealed class ChunkRankedProgress
{
    public IReadOnlyList<Chunk> Validated { get; set; } = [];
}
