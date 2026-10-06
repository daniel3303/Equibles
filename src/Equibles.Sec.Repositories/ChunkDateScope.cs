using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

internal static class ChunkDateScope
{
    private const int MaximumDocuments = 4096;

    internal static async Task<Guid[]> Read(
        EquiblesFinancialDbContext context,
        string ticker,
        Guid? documentId,
        IReadOnlyCollection<DocumentType> documentTypes,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken token
    )
    {
        if (startDate == null && endDate == null)
            return null;
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
        var ids = await documents
            .Select(document => document.Id)
            .Take(MaximumDocuments + 1)
            .ToArrayAsync(token);
        // A partial list would silently lose matches; large windows retain ranked-prefix validation.
        return ids.Length <= MaximumDocuments ? ids : null;
    }
}
