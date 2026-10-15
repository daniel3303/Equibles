using System.Globalization;
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
    // The index stores timestamps as signed 64-bit nanoseconds; ParadeDB 0.24 answers a bound
    // outside that span with no rows instead of an error.
    private static readonly DateOnly FirstIndexedDay = new(1677, 9, 22);
    private static readonly DateOnly LastIndexedDay = new(2262, 4, 11);

    internal static ParadeDbJsonQuery IndexFilter(DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate == null && endDate == null)
            return null;
        // Chunks carry their filing date at exactly midnight UTC, so a whole-day window is the
        // closed span between the first and last days' midnights. A bound outside the index's
        // timestamp span stays open: every chunk date lies inside it.
        var lower = startDate is { } start && start > FirstIndexedDay ? MidnightUtc(start) : null;
        var upper = endDate is { } end && end < LastIndexedDay ? MidnightUtc(end) : null;
        if (lower == null && upper == null)
            return null;
        return ParadeDbJsonQuery.Range(
            nameof(Chunk.ReportingDate),
            lower,
            upper,
            lowerInclusive: true,
            upperInclusive: true,
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
            documents = documents.ForListingSymbol(ticker, context);
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

    // Formatted here: the query builder renders a DateTime in the host's culture, which some
    // locales turn into a time ParadeDB rejects or a non-Gregorian year.
    private static string MidnightUtc(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";
}
