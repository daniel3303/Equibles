using System.Text;
using System.Text.RegularExpressions;
using Equibles.Core.Extensions;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Finite repair. Before the envelope parser read a whole inline document set, a filing that
/// tagged its cover in the primary document and its statements in an exhibit (a 40-F, many
/// 6-Ks) kept only the cover. Each periodic filing captured before <see cref="Cutoff"/> whose
/// facts name no currency is re-read from EDGAR once; a capture that grew is replaced and
/// re-extracted. A finished re-read stamps the capture-attempt ceiling so it never qualifies
/// again. Retire it when <see cref="Pending"/> is empty in production.
/// </summary>
public partial class XbrlDocumentSetRecaptureService
{
    // Captures written after this instant come from the document-set parser.
    internal static readonly DateTime Cutoff = new(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);

    private const int MaxFileNameLength = 256;

    [GeneratedRegex(@"/Archives/edgar/data/\d+/(\d{10}-\d{2}-\d{6})\.txt$", RegexOptions.IgnoreCase)]
    private static partial Regex EdgarSourceUrlAccession();

    private readonly DocumentRepository _documentRepository;
    private readonly FinancialFactRepository _financialFactRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly IFileManager _fileManager;
    private readonly ILogger<XbrlDocumentSetRecaptureService> _logger;

    public XbrlDocumentSetRecaptureService(
        DocumentRepository documentRepository,
        FinancialFactRepository financialFactRepository,
        ISecEdgarClient secEdgarClient,
        IFileManager fileManager,
        ILogger<XbrlDocumentSetRecaptureService> logger
    )
    {
        _documentRepository = documentRepository;
        _financialFactRepository = financialFactRepository;
        _secEdgarClient = secEdgarClient;
        _fileManager = fileManager;
        _logger = logger;
    }

    /// <summary>
    /// Captured inline periodic filings written before <see cref="Cutoff"/> with no fact in a
    /// three-letter currency unit; also the completion query for retiring this repair.
    /// </summary>
    public IQueryable<Document> Pending() =>
        _documentRepository
            .GetAll()
            .Where(d =>
                d.XbrlStatus == XbrlCaptureStatus.Captured
                && d.XbrlType == XbrlType.InlineIxbrl
                && d.XbrlContentId != null
                && d.XbrlContent.CreationTime < Cutoff
                && d.XbrlCaptureAttempts < Document.MaxXbrlCaptureAttempts
                && (
                    d.DocumentType == DocumentType.TenK
                    || d.DocumentType == DocumentType.TenKa
                    || d.DocumentType == DocumentType.TenQ
                    || d.DocumentType == DocumentType.TenQa
                    || d.DocumentType == DocumentType.TwentyF
                    || d.DocumentType == DocumentType.TwentyFa
                    || d.DocumentType == DocumentType.FortyF
                    || d.DocumentType == DocumentType.FortyFa
                    || d.DocumentType == DocumentType.SixK
                    || d.DocumentType == DocumentType.SixKa
                )
                && !_financialFactRepository
                    .GetAll()
                    .Any(fact =>
                        fact.DocumentId == d.Id
                        && fact.Unit.Length == 3
                        && fact.Unit.ToUpper() == fact.Unit
                    )
            );

    public async Task<XbrlDocumentSetRecaptureResult> Recapture(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var result = new XbrlDocumentSetRecaptureResult();
        if (batchSize <= 0)
            return result;

        // Annual reports first: a cover-only 40-F or 20-F leaves its issuer without statements
        // until re-read, while the queue is mostly 6-Ks.
        var batchIds = await Pending()
            .OrderByDescending(d =>
                d.DocumentType == DocumentType.TenK
                || d.DocumentType == DocumentType.TwentyF
                || d.DocumentType == DocumentType.FortyF
            )
            .ThenByDescending(d => d.ReportingDate)
            .Take(batchSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        foreach (var documentId in batchIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await _documentRepository
                .GetAll()
                .Include(d => d.Issuer)
                .Include(d => d.XbrlContent)
                .SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
            if (document == null)
                continue;

            result.Processed++;
            // A failure counts one attempt; only a finished re-read stamps the ceiling.
            var failedAttempts = document.XbrlCaptureAttempts + 1;
            try
            {
                if (await Recapture(document, cancellationToken))
                    result.Grown++;
                else
                    result.Unchanged++;
                await _documentRepository.SaveChanges();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.LogWarning(
                    ex,
                    "XBRL document-set recapture failed for document {DocumentId} ({Accession}), attempt {Attempt} of {MaxAttempts}.",
                    document.Id,
                    document.AccessionNumber,
                    failedAttempts,
                    Document.MaxXbrlCaptureAttempts
                );
                _documentRepository.ClearChangeTracker();
                await PersistAttempt(documentId, failedAttempts, cancellationToken);
            }
        }

        return result;
    }

    private async Task<bool> Recapture(Document document, CancellationToken cancellationToken)
    {
        var accession = document.AccessionNumber ?? AccessionFromSourceUrl(document.SourceUrl);
        if (accession == null || string.IsNullOrEmpty(document.Issuer?.Cik))
            throw new InvalidOperationException("The filing has no EDGAR accession or issuer CIK.");

        var submission = await _secEdgarClient.GetDocumentContent(
            accession,
            document.Issuer.Cik,
            cancellationToken
        );
        if (
            !SecDocumentEnvelopeParser.TryExtractXbrlEnvelope(
                submission,
                document.XbrlContent.Name,
                out var type,
                out var sourceFileName,
                out var body
            )
        )
            throw new InvalidOperationException("The submission no longer carries an XBRL envelope.");

        var raw = Encoding.UTF8.GetBytes(body);
        if (type == document.XbrlType && raw.LongLength == document.XbrlUncompressedSize)
        {
            document.XbrlCaptureAttempts = Document.MaxXbrlCaptureAttempts;
            return false;
        }

        var previous = document.XbrlContent;
        document.XbrlContent = await _fileManager.SaveInternalFile(
            GzipCompressor.Compress(raw),
            sourceFileName.TruncateToFit(MaxFileNameLength),
            "gz",
            "application/gzip"
        );
        document.XbrlType = type;
        document.XbrlUncompressedSize = raw.LongLength;
        document.XbrlFactsVersion = 0;
        document.XbrlFactsAttempts = 0;
        document.XbrlCaptureAttempts = Document.MaxXbrlCaptureAttempts;
        // Fact extraction keys the replacement's facts by the filing's accession.
        document.AccessionNumber ??= accession;
        _fileManager.DeleteFile(previous);
        return true;
    }

    private async Task PersistAttempt(Guid documentId, int attempts, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentRepository
                .GetAll()
                .SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
            if (document == null)
                return;
            document.XbrlCaptureAttempts = attempts;
            await _documentRepository.SaveChanges();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to persist the XBRL document-set recapture attempt.");
        }
    }

    private static string AccessionFromSourceUrl(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl))
            return null;
        var match = EdgarSourceUrlAccession().Match(sourceUrl);
        return match.Success ? match.Groups[1].Value : null;
    }
}
