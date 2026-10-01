using System.Diagnostics;
using Equibles.Data;
using Equibles.ParadeDB.EntityFrameworkCore;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Repositories;

internal static class ChunkRankedScope
{
    private const int InitialCandidateCount = 64;
    private const int MaximumCandidateCount = 65536;

    internal static async Task<List<Chunk>> Read(
        EquiblesFinancialDbContext context,
        IQueryable<Chunk> matching,
        int maxResults,
        string ticker,
        DateOnly? startDate,
        DateOnly? endDate,
        Stopwatch elapsed,
        int budgetSeconds,
        CancellationToken token
    )
    {
        if (maxResults <= 0)
            return [];
        var ranked = matching.OrderByDescending(chunk => EF.Functions.Score(chunk.Id));
        if (ticker == null && startDate == null && endDate == null)
            return await ranked.Take(maxResults).ToListAsync(token);

        var scoped = ParentScope(context, ticker, startDate, endDate);
        var limit = Math.Max(InitialCandidateCount, maxResults);
        var ceiling = Math.Max(MaximumCandidateCount, maxResults);
        while (true)
        {
            SetRemainingBudget(context, elapsed, budgetSeconds, token);
            // A join in this statement disables the index's TopK scan. Read only ranked keys;
            // validate the complete parent scope before admitting any candidate to the answer.
            var candidates = await ranked.Take(limit).Select(chunk => chunk.Id).ToListAsync(token);
            SetRemainingBudget(context, elapsed, budgetSeconds, token);
            var allowed = await scoped
                .Where(chunk => candidates.Contains(chunk.Id))
                .ToListAsync(token);
            var byId = allowed.ToDictionary(chunk => chunk.Id);
            var result = candidates
                .Distinct()
                .Where(byId.ContainsKey)
                .Take(maxResults)
                .Select(id => byId[id])
                .ToList();
            if (result.Count == maxResults || candidates.Count < limit)
                return result;
            if (limit == ceiling)
                throw new ChunkSearchTimeoutException(
                    "BM25 candidate budget exhausted before the filing scope was complete.",
                    null
                );
            // Re-read the ranked prefix: score ties need not keep their order across queries.
            // Combining offset pages could skip or duplicate tied candidates.
            limit = (int)Math.Min((long)limit * 2, ceiling);
        }
    }

    private static IQueryable<Chunk> ParentScope(
        EquiblesFinancialDbContext context,
        string ticker,
        DateOnly? startDate,
        DateOnly? endDate
    )
    {
        IQueryable<Document> documents = context.Set<Document>();
        if (ticker != null)
            documents = documents.ForUsTicker(ticker, context);
        if (startDate is { } start)
            documents = documents.Where(document => document.ReportingDate >= start);
        if (endDate is { } end)
            documents = documents.Where(document => document.ReportingDate <= end);
        var documentIds = documents.Select(document => document.Id);
        return context.Set<Chunk>().Where(chunk => documentIds.Contains(chunk.DocumentId));
    }

    private static void SetRemainingBudget(
        EquiblesFinancialDbContext context,
        Stopwatch elapsed,
        int budgetSeconds,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        var remaining = TimeSpan.FromSeconds(budgetSeconds) - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new ChunkSearchTimeoutException(
                $"BM25 chunk search exceeded its {budgetSeconds}s statement budget.",
                null
            );
        context.Database.SetCommandTimeout((int)Math.Ceiling(remaining.TotalSeconds));
    }
}
