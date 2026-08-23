using System.Text;
using System.Text.RegularExpressions;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Re-fetches and re-normalizes EDGAR documents whose stored Markdown predates the current
/// normalization pipeline. Each successful replacement keeps the document id, removes stale
/// chunks, and returns it to the indexed pending queue for locked reprocessing.
/// </summary>
public class DocumentNormalizationBackfillService
{
    private static readonly Regex EdgarSourceUrlAccession = new(
        @"/Archives/edgar/data/\d+/(\d{10}-\d{2}-\d{6})\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly DocumentRepository _documentRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ISecDocumentHtmlNormalizer _normalizer;
    private readonly ISecDocumentHtmlToMarkdownConverter _converter;
    private readonly IFileManager _fileManager;
    private readonly IDocumentPersistenceService _persistenceService;
    private readonly ILogger<DocumentNormalizationBackfillService> _logger;

    public DocumentNormalizationBackfillService(
        DocumentRepository documentRepository,
        ISecEdgarClient secEdgarClient,
        ISecDocumentHtmlNormalizer normalizer,
        ISecDocumentHtmlToMarkdownConverter converter,
        IFileManager fileManager,
        IDocumentPersistenceService persistenceService,
        ILogger<DocumentNormalizationBackfillService> logger
    )
    {
        _documentRepository = documentRepository;
        _secEdgarClient = secEdgarClient;
        _normalizer = normalizer;
        _converter = converter;
        _fileManager = fileManager;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task<DocumentNormalizationBackfillResult> Backfill(
        int batchSize,
        bool includeAllDocumentTypes = false,
        IReadOnlyCollection<string> priorityAccessions = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = new DocumentNormalizationBackfillResult();
        if (batchSize <= 0)
            return result;

        var pending = _documentRepository.GetPendingNormalizedContent();
        var priorities =
            priorityAccessions
                ?.Where(accession => !string.IsNullOrWhiteSpace(accession))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];

        var priorityIds = new List<Guid>();
        foreach (var accession in priorities.Take(batchSize))
        {
            var sourceSuffix = $"/{accession}.txt";
            var priorityId = await pending
                .Where(d =>
                    !priorityIds.Contains(d.Id)
                    && (
                        d.AccessionNumber == accession
                        || (
                            (d.AccessionNumber == null || d.AccessionNumber == "")
                            && d.SourceUrl != null
                            && d.SourceUrl.EndsWith(sourceSuffix)
                        )
                    )
                )
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (priorityId.HasValue)
            {
                priorityIds.Add(priorityId.Value);
            }
        }

        var remaining = batchSize - priorityIds.Count;
        var stagedIds = new List<Guid>();
        if (remaining > 0)
        {
            stagedIds = await _documentRepository
                .GetOrderedPendingNormalizedContent(includeAllDocumentTypes)
                .Where(d => !priorityIds.Contains(d.Id))
                .Take(remaining)
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);
        }

        var batchIds = priorityIds.Concat(stagedIds);

        foreach (var documentId in batchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var document = await _documentRepository.GetWithContent(documentId, cancellationToken);
            if (document == null)
                continue;

            result.Processed++;
            document.NormalizedContentAttempts++;
            var currentAttempt = document.NormalizedContentAttempts;
            if (string.IsNullOrEmpty(document.AccessionNumber))
            {
                document.AccessionNumber = DeriveAccessionNumber(document.SourceUrl);
            }

            if (document.AccessionNumber == null)
            {
                result.Failed++;
                _logger.LogWarning(
                    "Document normalization backfill cannot derive an accession number from SourceUrl {SourceUrl} for document {DocumentId}.",
                    document.SourceUrl,
                    document.Id
                );
                await RevertAndPersistAttempt(document, cancellationToken);
                continue;
            }

            try
            {
                var source = await _secEdgarClient.GetDocumentContent(
                    document.AccessionNumber,
                    document.CommonStock.Cik,
                    cancellationToken
                );
                var normalizedHtml = _normalizer.Normalize(source);
                var markdown = _converter.Convert(normalizedHtml);
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    throw new InvalidOperationException(
                        $"Normalization produced no content for document {document.Id}."
                    );
                }

                var normalizedContent = Encoding.UTF8.GetBytes(markdown);
                document.NormalizedContentVersion = Document.NormalizedContentBuilderVersion;
                document.NormalizedContentAttempts = 0;

                if (await ContentMatches(document, normalizedContent))
                {
                    await _persistenceService.ResetChunks(document, cancellationToken);
                    result.Unchanged++;
                    continue;
                }

                await _persistenceService.ReplaceContent(
                    document,
                    normalizedContent,
                    cancellationToken
                );
                result.Replaced++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed++;
                // Success bookkeeping is applied before persistence so it commits atomically with
                // the corrected content. Restore this run's attempt if comparison or persistence
                // fails, otherwise deterministic storage failures can retry forever.
                document.NormalizedContentAttempts = currentAttempt;
                _logger.LogWarning(
                    ex,
                    "Document normalization backfill failed for document {DocumentId} ({Accession}); will retry.",
                    document.Id,
                    document.AccessionNumber
                );
                await RevertAndPersistAttempt(document, cancellationToken);
            }
        }

        return result;
    }

    private async Task<bool> ContentMatches(Document document, byte[] normalizedContent)
    {
        if (document.Content == null)
            return false;

        var storedContent = await _fileManager.GetContent(document.Content);
        return storedContent.AsSpan().SequenceEqual(normalizedContent);
    }

    private static string DeriveAccessionNumber(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl))
            return null;

        var match = EdgarSourceUrlAccession.Match(sourceUrl);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task RevertAndPersistAttempt(
        Document document,
        CancellationToken cancellationToken
    )
    {
        _documentRepository.ClearChangeTracker();
        try
        {
            await _documentRepository.PersistNormalizedContentAttempt(document, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist document normalization backfill attempt count."
            );
        }
    }
}
