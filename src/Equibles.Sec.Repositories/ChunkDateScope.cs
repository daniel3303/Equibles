using Equibles.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

// A filing-date window reaches the BM25 index as a range on Chunk.ReportingDate, which the
// Document trigger keeps equal to the parent date; the parent stays the membership authority.
internal static class ChunkDateScope
{
    internal static ParadeDbJsonQuery IndexFilter(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate == null && endDate == null)
            return null;
        // Chunks carry their filing date at midnight UTC, so a whole-day window is the half-open
        // span from the first day's midnight to the midnight after the last day.
        return ParadeDbJsonQuery.Range(
            nameof(Chunk.ReportingDate),
            startDate is { } start ? MidnightUtc(start) : null,
            endDate is { } end ? MidnightUtc(end.AddDays(1)) : null,
            lowerInclusive: true,
            upperInclusive: false,
            isDatetime: true
        );
    }

    // One indexed probe so an empty window answers without touching the BM25 index.
    internal static Task<bool> HasParents(
        EquiblesFinancialDbContext context,
        string ticker,
        Guid? documentId,
        IReadOnlyCollection<DocumentType> documentTypes,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken token
    )
    {
        IQueryable<Document> documents = context.Set<Document>();
        if (ticker != null)
            documents = documents.ForUsTicker(ticker, context);
        if (documentId is { } id)
            documents = documents.Where(document => document.Id == id);
        if (documentTypes is { Count: > 0 })
            documents = documents.Where(document => documentTypes.Contains(document.DocumentType));
        if (startDate is { } start)
            documents = documents.Where(document => document.ReportingDate >= start);
        if (endDate is { } end)
            documents = documents.Where(document => document.ReportingDate <= end);
        return documents.AnyAsync(token);
    }

    private static DateTime MidnightUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
}
