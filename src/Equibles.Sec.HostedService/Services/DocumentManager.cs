using System.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

[Service]
public class DocumentManager
{
    private const int DefaultLoadSize = 1024;

    private readonly DocumentRepository _documentRepository;
    private readonly ChunkRepository _chunkRepository;
    private readonly BackfillStateRepository _backfillStateRepository;
    private readonly IDocumentProcessor _documentProcessor;
    private readonly EmbeddingConfig _embeddingConfig;
    private readonly int _loadSize;
    private readonly ILogger<DocumentManager> _logger;

    public DocumentManager(
        DocumentRepository documentRepository,
        ChunkRepository chunkRepository,
        BackfillStateRepository backfillStateRepository,
        IDocumentProcessor documentProcessor,
        IOptions<EmbeddingConfig> embeddingConfig,
        ILogger<DocumentManager> logger
    )
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _backfillStateRepository = backfillStateRepository;
        _documentProcessor = documentProcessor;
        _embeddingConfig = embeddingConfig.Value;
        _loadSize = Math.Max(DefaultLoadSize, _embeddingConfig.BatchSize);
        _logger = logger;
    }

    // The chunk loader reads the partial pending index. Existing installations initially have a
    // null marker on every document, so a bounded compatibility update marks legacy rows that
    // already have chunks while holding their document row locks. This drains the corpus without
    // an unbounded anti-join and makes concurrent reset/replacement authoritative.

    public async Task<bool> ChunkDocumentBatch(CancellationToken cancellationToken)
    {
        if (await BackfillChunkedAtBatch(cancellationToken) > 0)
            return true;

        var pendingDocumentIds = await _documentRepository
            .GetAll()
            .Where(d =>
                d.ChunkedAt == null
                && d.Content != null
                && d.ChunkAttempts < Document.MaxChunkAttempts
            )
            .OrderBy(d => d.CreationTime)
            .ThenBy(d => d.Id)
            .Select(d => d.Id)
            .Take(_loadSize)
            .ToListAsync(cancellationToken);

        if (pendingDocumentIds.Count == 0)
            return false;

        _logger.LogInformation("Chunking {Count} documents", pendingDocumentIds.Count);
        var attempted = 0;
        var progressed = 0;
        foreach (var documentId in pendingDocumentIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await using var transaction = await _documentRepository.CreateTransaction(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );
            try
            {
                var document = await _documentRepository.GetPendingForUpdate(
                    documentId,
                    cancellationToken
                );
                if (document == null)
                    continue;

                attempted++;
                await _documentProcessor.ProcessDocument(document, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                progressed++;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error chunking document {DocumentId}", documentId);

                // A shutdown mid-chunk is not a document fault, so only a genuine failure
                // consumes retry budget.
                if (!cancellationToken.IsCancellationRequested)
                {
                    await RecordChunkAttempt(documentId, cancellationToken);
                }
            }
            finally
            {
                _documentRepository.ClearChangeTracker();
            }
        }

        if (!cancellationToken.IsCancellationRequested && attempted > 0 && progressed == 0)
        {
            throw new InvalidOperationException(
                $"Chunking failed for all {attempted} documents in the batch — the content "
                    + "store is likely unavailable. Backing off this cycle."
            );
        }

        return progressed > 0;
    }

    private Task<int> BackfillChunkedAtBatch(CancellationToken cancellationToken) =>
        _documentRepository.BackfillLegacyChunked(_loadSize, DateTime.UtcNow, cancellationToken);

    // The failed transaction rolled back every tracked change, so the retry bookkeeping is
    // persisted separately, exactly like the normalization lane's attempt counter. A document
    // that reaches the ceiling stays pending-but-parked: visible to queries, no longer selected.
    private async Task RecordChunkAttempt(Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var attempts = await _documentRepository.PersistChunkAttempt(
                documentId,
                cancellationToken
            );
            if (attempts >= Document.MaxChunkAttempts)
            {
                _logger.LogWarning(
                    "Document {DocumentId} failed chunking {Attempts} times and leaves the "
                        + "pending queue; a content replacement or chunk reset returns it "
                        + "with a fresh budget",
                    documentId,
                    attempts
                );
            }
        }
        catch (Exception ex)
        {
            // Bookkeeping must never mask the original failure; the document simply stays
            // pending and the next cycle retries both the chunking and this counter.
            _logger.LogError(
                ex,
                "Failed to record the chunk attempt for document {DocumentId}",
                documentId
            );
        }
    }

    public async Task<bool> GenerateEmbeddingBatch(
        BackfillCursor cursor,
        CancellationToken cancellationToken
    )
    {
        if (!_embeddingConfig.IsConfigured)
            return false;

        var chunksWithoutEmbeddings = await LoadBatch(
            floor =>
            {
                var query = _chunkRepository.GetAll().Where(c => !c.Embeddings.Any());
                if (floor is { } f)
                    query = query.Where(c => c.CreationTime >= f);
                return query
                    .OrderBy(c => c.CreationTime)
                    .Take(_loadSize)
                    .ToListAsync(cancellationToken);
            },
            cursor
        );

        if (chunksWithoutEmbeddings.Count == 0)
            return false;

        _logger.LogInformation(
            "Generating embeddings for {Count} chunks",
            chunksWithoutEmbeddings.Count
        );
        await ProcessOrRewind(
            () => _documentProcessor.GenerateEmbeddings(chunksWithoutEmbeddings, cancellationToken),
            cursor
        );
        cursor.Advance(chunksWithoutEmbeddings[^1].CreationTime);
        await PersistCursor(cursor);
        return true;
    }

    // An all-fail batch throws (systemic outage) and the cursor then never advances past it.
    // If that batch came from the daily full rescan its slot was already stamped, so without
    // a rewind the stranded rows wait a whole day per fault — the #4143 starvation, moved one
    // step downstream from the scan to its processing. The caller can't tell which tier
    // produced the batch, and rewinding unconditionally is safe: for floored/bounded batches
    // it merely pulls the next full rescan earlier.
    private async Task ProcessOrRewind(Func<Task> process, BackfillCursor cursor)
    {
        try
        {
            await process();
        }
        catch
        {
            cursor.MarkFullRescanFailed(DateTime.UtcNow);
            await TryPersistCursor(cursor);
            throw;
        }
    }

    // Floored batch first; when the frontier drains, an hourly rescan bounded a week behind the
    // floor catches near-frontier stragglers cheaply, and an unfloored corpus scan runs at most
    // daily as the backstop for re-queued work older than the bounded window. The cursor
    // hydrates from its persisted BackfillState row on first use per process, so a restart
    // resumes at the frontier instead of paying the corpus scan.
    private async Task<List<T>> LoadBatch<T>(
        Func<DateTime?, Task<List<T>>> query,
        BackfillCursor cursor
    )
    {
        await HydrateCursor(cursor);

        if (cursor.Floor is { } floor)
        {
            var batch = await query(floor);
            if (batch.Count > 0)
                return batch;
        }

        var utcNow = DateTime.UtcNow;
        if (cursor.Floor is { } drainedFloor && cursor.TryStartBoundedRescan(utcNow))
        {
            var batch = await query(drainedFloor - BackfillCursor.BoundedRescanLookback);
            if (batch.Count > 0)
                return batch;
        }

        if (!cursor.TryStartFullRescan(utcNow))
            return [];

        // Stamp before scanning: an interrupted scan then waits out an interval instead of a
        // crash-loop re-running the minutes-long scan on every boot; fresh work still flows
        // through the floored and bounded tiers regardless.
        await PersistCursor(cursor);
        try
        {
            return await query(null);
        }
        catch
        {
            // A failed scan must not consume the whole daily slot: rows behind the bounded
            // window are reachable only here, so charging every fault a full interval starves
            // them indefinitely under a recurring query timeout. Rewind the stamp to a short
            // retry spacing and let the fault propagate to the worker's error ladder.
            cursor.MarkFullRescanFailed(utcNow);
            await TryPersistCursor(cursor);
            throw;
        }
    }

    // Persist that must never mask an in-flight scan fault: if the store is down the original
    // exception carries the diagnosis, and the rewound stamp still lives on the in-memory
    // cursor for this process.
    private async Task TryPersistCursor(BackfillCursor cursor)
    {
        try
        {
            await PersistCursor(cursor);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not persist the rewound full-rescan stamp for {Cursor}",
                cursor.Name
            );
        }
    }

    private async Task HydrateCursor(BackfillCursor cursor)
    {
        if (cursor.IsHydrated)
            return;

        var state = await _backfillStateRepository.GetByName(cursor.Name);
        cursor.Hydrate(state?.Floor, state?.LastFullRescanAt);
    }

    private async Task PersistCursor(BackfillCursor cursor)
    {
        var state = await _backfillStateRepository.GetByName(cursor.Name);
        var isNew = state == null;
        if (isNew)
            state = new BackfillState { Name = cursor.Name };

        state.Floor = cursor.Floor;
        state.LastFullRescanAt = cursor.LastFullRescanAt;

        // Mark the write explicitly rather than leaning on change tracking: a floor advance
        // that silently failed to persist would re-hydrate the stale floor on the next restart
        // and re-run the corpus scan this fix exists to prevent, with no test catching it.
        if (isNew)
            _backfillStateRepository.Add(state);
        else
            _backfillStateRepository.Update(state);
        await _backfillStateRepository.SaveChanges();
    }
}
