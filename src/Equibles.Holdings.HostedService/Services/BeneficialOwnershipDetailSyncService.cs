using System.Net;
using System.Text;
using System.Xml;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// Fills in cover-page details for stored 13D/13G filings that have none, newest first: the
/// history imported before details were kept, and any filing whose realtime write failed.
/// </summary>
[Service]
public class BeneficialOwnershipDetailSyncService
{
    private const string PrimaryDocument = "primary_doc.xml";

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ISecEdgarClient _edgarClient;
    private readonly Filing13DGXmlParser _parser;
    private readonly BeneficialOwnershipDetailWriter _writer;
    private readonly ILogger<BeneficialOwnershipDetailSyncService> _logger;

    public BeneficialOwnershipDetailSyncService(
        EquiblesFinancialDbContext dbContext,
        ISecEdgarClient edgarClient,
        Filing13DGXmlParser parser,
        BeneficialOwnershipDetailWriter writer,
        ILogger<BeneficialOwnershipDetailSyncService> logger
    )
    {
        _dbContext = dbContext;
        _edgarClient = edgarClient;
        _parser = parser;
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// Reads up to <paramref name="maxFilings"/> filings without details and returns true when the
    /// worker should continue at once: more remain and this pass stored something. An EDGAR outage
    /// ends the pass and leaves the rest for the next cycle.
    /// </summary>
    public async Task<bool> SyncMissing(int maxFilings, CancellationToken cancellationToken)
    {
        var candidates = await _dbContext
            .Set<InstitutionalFiling>()
            .AsNoTracking()
            .Where(filing => filing.FilingType != FilingType.Form13F)
            .Where(filing =>
                !_dbContext
                    .Set<BeneficialOwnershipFiling>()
                    .Any(detail => detail.AccessionNumber == filing.AccessionNumber)
            )
            .OrderByDescending(filing => filing.FilingDate)
            .ThenByDescending(filing => filing.AccessionNumber)
            .Select(filing => new DetailCandidate(
                filing.AccessionNumber,
                filing.FilingType,
                filing.FilingDate,
                filing.InstitutionalHolder.Cik
            ))
            .Take(maxFilings + 1)
            .ToListAsync(cancellationToken);

        var stored = 0;
        var unreadable = 0;
        var skipped = 0;
        foreach (var candidate in candidates.Take(maxFilings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await Sync(candidate, cancellationToken))
                    stored++;
                else
                    unreadable++;
            }
            catch (HttpRequestException exception) when (IsOutage(exception))
            {
                _logger.LogWarning(
                    exception,
                    "13D/13G detail sync stopped: EDGAR is unavailable ({Status}) after {Stored} filings",
                    exception.StatusCode,
                    stored
                );
                return false;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A timeout or a failed write is retried on a later pass, never marked unreadable.
                _logger.LogWarning(
                    exception,
                    "13D/13G detail sync skipped {Accession} this pass",
                    candidate.AccessionNumber
                );
                skipped++;
            }
        }

        if (candidates.Count > 0)
            _logger.LogInformation(
                "13D/13G detail sync stored {Stored} filings, marked {Unreadable} unreadable and skipped {Skipped}",
                stored,
                unreadable,
                skipped
            );

        // Documents that keep failing stay at the head of the order, so a pass that stored nothing
        // waits for the next cycle instead of fetching them again at once.
        return candidates.Count > maxFilings && stored + unreadable > 0;
    }

    // Returns true when the details were stored, false when the filing was recorded as unreadable.
    private async Task<bool> Sync(DetailCandidate candidate, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = string.IsNullOrWhiteSpace(candidate.Cik)
                ? []
                : await _edgarClient.GetDocumentFileBytes(
                    candidate.Cik,
                    candidate.AccessionNumber,
                    PrimaryDocument,
                    cancellationToken
                );
        }
        catch (HttpRequestException exception) when (!IsOutage(exception))
        {
            // Any refusal other than an outage is final for the document, like a 404.
            _logger.LogInformation(
                "EDGAR refused 13D/13G {Accession} ({Status})",
                candidate.AccessionNumber,
                exception.StatusCode
            );
            bytes = [];
        }

        var filing = TryParse(
            bytes,
            candidate.AccessionNumber,
            candidate.Cik,
            candidate.FilingDate
        );
        if (filing == null)
        {
            // A missing or unparseable document never changes; record it so it is not refetched.
            await _writer.WriteUnreadable(
                candidate.AccessionNumber,
                candidate.FilingType,
                candidate.FilingDate,
                candidate.Cik,
                cancellationToken
            );
            return false;
        }

        await _writer.Write(filing, cancellationToken);
        return true;
    }

    private Parsed13DGFiling TryParse(
        byte[] bytes,
        string accessionNumber,
        string cik,
        DateOnly filingDate
    )
    {
        if (bytes.Length == 0)
            return null;

        try
        {
            return _parser.ParseFiling(
                Encoding.UTF8.GetString(bytes),
                accessionNumber,
                cik,
                filingDate
            );
        }
        catch (Exception exception) when (exception is XmlException or FormatException)
        {
            _logger.LogWarning(exception, "13D/13G {Accession} cannot be parsed", accessionNumber);
            return null;
        }
    }

    // A missing status, a server error, a throttle or a 403 (SEC's rate-limit answer) is an outage.
    private static bool IsOutage(HttpRequestException exception) =>
        exception.StatusCode
            is null
                or >= HttpStatusCode.InternalServerError
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.Forbidden;
}
