using System.Data;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Messaging.Contracts.Sec;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using MassTransit;

namespace Equibles.Sec.HostedService.Services;

public class DocumentPersistenceService : IDocumentPersistenceService
{
    private const int MaxFileNameLength = 256;

    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;
    private readonly IBus _bus;

    public DocumentPersistenceService(
        DocumentRepository documentRepository,
        IFileManager fileManager,
        IBus bus
    )
    {
        _documentRepository = documentRepository;
        _fileManager = fileManager;
        _bus = bus;
    }

    public Task<bool> Exists(
        CommonStock company,
        DocumentType documentType,
        DateOnly reportingDate,
        DateOnly reportingForDate
    )
    {
        return _documentRepository.Exists(company, documentType, reportingDate, reportingForDate);
    }

    public async Task Save(
        CommonStock company,
        byte[] content,
        string fileName,
        DocumentType documentType,
        DateOnly reportingDate,
        DateOnly reportingForDate,
        string sourceUrl,
        string accessionNumber = null,
        string items = null,
        XbrlCaptureResult xbrl = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var transaction = await _documentRepository.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        var file = await _fileManager.SaveFile(content, fileName);
        var lineCount = Encoding.UTF8.GetString(content).Split('\n').Length;

        var document = new Document
        {
            CommonStock = company,
            Content = file,
            DocumentType = documentType,
            ReportingDate = reportingDate,
            ReportingForDate = reportingForDate,
            SourceUrl = sourceUrl,
            AccessionNumber = accessionNumber,
            Items = items,
            LineCount = lineCount,
        };

        await ApplyXbrlCapture(document, xbrl ?? XbrlCaptureResult.NotChecked);

        _documentRepository.Add(document);
        await _documentRepository.SaveChanges();
        await transaction.CommitAsync(cancellationToken);

        // Announce the save after the insert commits — OSS has no transactional outbox, so a
        // pre-commit publish could fire on a rolled-back insert. A consumer that misses this
        // event (process crash between commit and publish) is reconciled by the backfill.
        await _bus.Publish(
            new DocumentSaved(
                document.Id,
                company.Id,
                company.Ticker,
                documentType.Value,
                reportingDate,
                reportingForDate,
                accessionNumber,
                items
            ),
            cancellationToken
        );
    }

    public async Task UpdateXbrl(Document document, XbrlCaptureResult xbrl)
    {
        await ApplyXbrlCapture(document, xbrl ?? XbrlCaptureResult.NotChecked);
        await _documentRepository.SaveChanges();
    }

    // Stores the captured XBRL envelope as a gzip-compressed internal File and records its
    // type/sizes on the document. NotChecked/NotPresent only set the status — no File is
    // created — so the document either stays a backfill target or is marked terminally empty.
    private async Task ApplyXbrlCapture(Document document, XbrlCaptureResult xbrl)
    {
        if (xbrl.Status != XbrlCaptureStatus.Captured || xbrl.RawBytes == null)
        {
            document.XbrlStatus = xbrl.Status;
            return;
        }

        var compressed = GzipCompressor.Compress(xbrl.RawBytes);
        // File.Name is capped at 256 chars; EDGAR document names are bare short tokens, but
        // guard against a pathological envelope value so the insert can never overflow. A raw
        // slice can split a surrogate pair, leaving an orphan high surrogate that corrupts the
        // UTF-8 round-trip on insert — back off one unit when the cut lands mid-pair (mirrors
        // EdgarXmlSubmissionParser.Truncate).
        var name = xbrl.SourceFileName;
        if (name?.Length > MaxFileNameLength)
        {
            var end = char.IsHighSurrogate(name[MaxFileNameLength - 1])
                ? MaxFileNameLength - 1
                : MaxFileNameLength;
            name = name[..end];
        }
        var xbrlFile = await _fileManager.SaveInternalFile(
            compressed,
            name,
            "gz",
            "application/gzip"
        );

        document.XbrlContent = xbrlFile;
        document.XbrlType = xbrl.Type;
        document.XbrlUncompressedSize = xbrl.RawBytes.Length;
        document.XbrlStatus = XbrlCaptureStatus.Captured;
    }
}
