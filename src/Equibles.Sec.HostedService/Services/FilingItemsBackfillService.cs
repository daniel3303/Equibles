using System.Text.RegularExpressions;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Stamps <c>Document.Items</c> onto 8-K rows ingested before item capture went live, so the
/// earnings-release consumers (Item 2.02) can match historical filings. Each cycle picks the
/// companies with pending 8-Ks (newest filings first), re-fetches each company's submissions
/// feed once — <see cref="ISecEdgarClient.GetCompanyFilings"/> already walks the archive pages,
/// so the whole filing history is covered — and stamps every pending 8-K by accession number.
/// </summary>
/// <remarks>
/// A document whose accession is absent from the feed (or carries no items there) is stamped
/// with the empty string: a terminal "checked, nothing found" marker distinct from the null
/// "never checked" state, so the corpus drains instead of re-selecting the same companies
/// forever. Consumers treat empty and null alike (no items), so the marker changes nothing
/// for them. The sweep is deliberately not bounded by the worker minimum sync date — stamping
/// must cover every pending 8-K of a selected company, or the leftovers would re-select the
/// company every cycle.
/// </remarks>
public class FilingItemsBackfillService
{
    // Rows ingested before AccessionNumber existed carry it only inside the stored EDGAR
    // full-submission URL; the accession is the file name. Same recovery as the XBRL
    // backfill — kept as a private copy because that one is pinned in place by tests.
    private static readonly Regex EdgarSourceUrlAccession = new(
        @"/Archives/edgar/data/\d+/(\d{10}-\d{2}-\d{6})\.txt$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly DocumentRepository _documentRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FilingItemsBackfillService> _logger;

    public FilingItemsBackfillService(
        DocumentRepository documentRepository,
        ISecEdgarClient secEdgarClient,
        ILogger<FilingItemsBackfillService> logger
    )
    {
        _documentRepository = documentRepository;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    public async Task<FilingItemsBackfillResult> Backfill(
        int companyBatchSize,
        CancellationToken cancellationToken = default
    )
    {
        var result = new FilingItemsBackfillResult();
        if (companyBatchSize <= 0)
        {
            return result;
        }

        // Companies with pending 8-Ks, ordered by their most recent filing so recent
        // quarters become linkable soonest. Companies without a CIK can never be fetched
        // and are simply never selected.
        var companies = await PendingEightKs()
            .GroupBy(d => d.CommonStockId)
            .Select(g => new { StockId = g.Key, LatestReportingDate = g.Max(d => d.ReportingDate) })
            .OrderByDescending(x => x.LatestReportingDate)
            .Take(companyBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var company in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Per-company failures are isolated: the documents stay null and the company is
            // retried on a later cycle, while the rest of the batch still progresses.
            try
            {
                await BackfillCompany(company.StockId, result, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Failed++;
                _logger.LogWarning(
                    ex,
                    "Filing-items backfill failed for stock {StockId}; will retry next cycle.",
                    company.StockId
                );
            }
        }

        return result;
    }

    private async Task BackfillCompany(
        Guid stockId,
        FilingItemsBackfillResult result,
        CancellationToken cancellationToken
    )
    {
        var documents = await PendingEightKs()
            .Where(d => d.CommonStockId == stockId)
            .Include(d => d.CommonStock)
            .ToListAsync(cancellationToken);
        if (documents.Count == 0)
        {
            return;
        }

        var stock = documents[0].CommonStock;
        var filings = await _secEdgarClient.GetCompanyFilings(stock.Cik, DocumentTypeFilter.EightK);
        var itemsByAccession = filings
            .Where(f => !string.IsNullOrEmpty(f.AccessionNumber))
            .DistinctBy(f => f.AccessionNumber)
            .ToDictionary(f => f.AccessionNumber, f => f.Items);

        var stamped = 0;
        foreach (var document in documents)
        {
            // Recover the accession for legacy rows; it is persisted alongside the items so
            // later consumers can link the filing without re-deriving.
            document.AccessionNumber ??= DeriveAccessionNumber(document.SourceUrl);

            string items = null;
            if (document.AccessionNumber != null)
            {
                itemsByAccession.TryGetValue(document.AccessionNumber, out items);
            }

            if (string.IsNullOrWhiteSpace(items))
            {
                // Terminal marker — see the class remarks.
                document.Items = string.Empty;
                result.NotFound++;
            }
            else
            {
                document.Items = items;
                stamped++;
            }
        }

        await _documentRepository.SaveChanges();
        result.Companies++;
        result.Stamped += stamped;

        _logger.LogInformation(
            "Filing-items backfill stamped {Stamped} of {Total} pending 8-Ks for {Ticker}.",
            stamped,
            documents.Count,
            stock.Ticker
        );
    }

    private IQueryable<Document> PendingEightKs() =>
        _documentRepository
            .GetByDocumentType(DocumentType.EightK)
            .Where(d => d.Items == null && d.CommonStock.Cik != null);

    private static string DeriveAccessionNumber(string sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl))
        {
            return null;
        }

        var match = EdgarSourceUrlAccession.Match(sourceUrl);
        return match.Success ? match.Groups[1].Value : null;
    }
}
