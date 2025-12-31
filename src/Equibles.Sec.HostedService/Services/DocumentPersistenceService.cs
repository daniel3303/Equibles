using System.Data;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Repositories;

namespace Equibles.Sec.HostedService.Services;

public class DocumentPersistenceService : IDocumentPersistenceService {
    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;

    public DocumentPersistenceService(DocumentRepository documentRepository, IFileManager fileManager) {
        _documentRepository = documentRepository;
        _fileManager = fileManager;
    }

    public Task<bool> Exists(CommonStock company, DocumentType documentType, DateOnly reportingDate,
        DateOnly reportingForDate
    ) {
        return _documentRepository.Exists(company, documentType, reportingDate, reportingForDate);
    }

    public async Task Save(CommonStock company, byte[] content, string fileName, DocumentType documentType,
        DateOnly reportingDate, DateOnly reportingForDate, string sourceUrl, CancellationToken cancellationToken = default
    ) {
        await using var transaction = await _documentRepository.CreateTransaction(IsolationLevel.ReadCommitted, cancellationToken);

        var file = await _fileManager.SaveFile(content, fileName);
        var lineCount = Encoding.UTF8.GetString(content).Split('\n').Length;

        var document = new Document {
            CommonStock = company,
            Content = file,
            DocumentType = documentType,
            ReportingDate = reportingDate,
            ReportingForDate = reportingForDate,
            SourceUrl = sourceUrl,
            LineCount = lineCount
        };

        _documentRepository.Add(document);
        await _documentRepository.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
    }
}
