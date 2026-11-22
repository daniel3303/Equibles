using System.Text;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Dates fund reports stored with no period (EDGAR's index left it blank) from the period their cover states, the
/// same reading ingestion now applies. A cover that states none leaves its report undated, and the next pass reads it
/// again; only fund reports with an accession are read, since a row without one is matched on its period when the
/// scraper checks for known filings.
/// </summary>
public class FundReportPeriodBackfillService
{
    private const int PageSize = 50;

    private readonly DocumentRepository _documentRepository;
    private readonly IFileManager _fileManager;
    private readonly ILogger<FundReportPeriodBackfillService> _logger;

    public FundReportPeriodBackfillService(
        DocumentRepository documentRepository,
        IFileManager fileManager,
        ILogger<FundReportPeriodBackfillService> logger
    )
    {
        _documentRepository = documentRepository;
        _fileManager = fileManager;
        _logger = logger;
    }

    public async Task<FundReportPeriodBackfillResult> Backfill(
        CancellationToken cancellationToken = default
    )
    {
        var result = new FundReportPeriodBackfillResult();
        var undated = await Undated()
            .OrderBy(d => d.Id)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);
        foreach (var ids in undated.Chunk(PageSize))
        {
            var page = await Undated()
                .Where(d => ids.Contains(d.Id))
                .Include(d => d.Content)
                .ToListAsync(cancellationToken);

            foreach (var document in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Selected++;
                string text;
                try
                {
                    text = Encoding.UTF8.GetString(await _fileManager.GetContent(document.Content));
                }
                // An unreadable stored file fails the same way on every pass: it skips this report, never the rest.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.Unreadable++;
                    _logger.LogWarning(
                        ex,
                        "Fund report {DocumentId} ({AccessionNumber}): its stored text could not be read; it stays undated.",
                        document.Id,
                        document.AccessionNumber
                    );
                    continue;
                }

                if (FundReportCoverPeriod.Read(text, document.ReportingDate) is { } period)
                {
                    document.ReportingForDate = period;
                    result.Dated++;
                }
                else
                {
                    _logger.LogInformation(
                        "Fund report {DocumentId} ({AccessionNumber}) states no period on its cover; it stays undated.",
                        document.Id,
                        document.AccessionNumber
                    );
                }
            }

            await _documentRepository.SaveChanges();
        }

        return result;
    }

    private IQueryable<Document> Undated() =>
        _documentRepository
            .GetAll()
            .Where(d =>
                FundReportCoverPeriod.Forms.Contains(d.DocumentType)
                && d.ReportingForDate == DateOnly.MinValue
                && d.Content != null
                && d.AccessionNumber != null
                && d.AccessionNumber != ""
            );
}
