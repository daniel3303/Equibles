using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.ParadeDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

public class ChunkRepository : BaseRepository<Chunk> {
    public ChunkRepository(EquiblesDbContext dbContext) : base(dbContext) {
    }

    public async Task<List<Chunk>> HybridSearch(string searchText, int maxResults, string ticker = null,
        Guid? documentId = null, DocumentType documentType = null,
        DateOnly? startDate = null, DateOnly? endDate = null
    ) {
        var query = DbContext.Set<Chunk>()
            .Where(c => EF.Functions.Parse(c.Id, searchText, lenient: true, conjunctionMode: true));

        if (ticker != null)
            query = query.Where(c => c.Ticker == ticker);

        if (documentId.HasValue)
            query = query.Where(c => c.DocumentId == documentId.Value);

        if (documentType != null)
            query = query.Where(c => c.DocumentType == documentType);

        if (startDate.HasValue) {
            var startUtc = DateTime.SpecifyKind(startDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(c => c.ReportingDate >= startUtc);
        }

        if (endDate.HasValue) {
            var endUtc = DateTime.SpecifyKind(endDate.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            query = query.Where(c => c.ReportingDate <= endUtc);
        }

        return await query
            .OrderByDescending(c => EF.Functions.Score(c.Id))
            .Take(maxResults)
            .ToListAsync();
    }
}
