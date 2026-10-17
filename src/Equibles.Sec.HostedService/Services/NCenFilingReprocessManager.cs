using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Re-reads stored N-CEN filings whose <see cref="NCenFiling.ParserVersion"/> is below
/// <see cref="NCenFiling.CurrentParserVersion"/>: each is fetched from EDGAR again, parsed through
/// <see cref="NCenFilingProcessor.ParseEntity"/>, and its facts and providers replaced in one save.
/// </summary>
[Service]
public class NCenFilingReprocessManager
{
    private const int BatchSize = 32;

    // A filing that still fails after this many passes keeps its stored facts and is stamped current,
    // so a pulled or malformed submission cannot hold the backlog.
    internal const int MaxReprocessAttempts = 3;

    private readonly NCenFilingRepository _filingRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NCenFilingReprocessManager> _logger;

    public NCenFilingReprocessManager(
        NCenFilingRepository filingRepository,
        ISecEdgarClient secEdgarClient,
        EquiblesFinancialDbContext dbContext,
        ErrorReporter errorReporter,
        ILogger<NCenFilingReprocessManager> logger
    )
    {
        _filingRepository = filingRepository;
        _secEdgarClient = secEdgarClient;
        _dbContext = dbContext;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    public async Task<NCenFilingReprocessResult> Run(CancellationToken cancellationToken = default)
    {
        var result = new NCenFilingReprocessResult
        {
            Total = await Stale().CountAsync(cancellationToken),
        };

        // A failed filing is skipped for the rest of this pass, so the pass always terminates.
        var failedThisRun = new HashSet<Guid>();
        while (result.Total > 0 && !cancellationToken.IsCancellationRequested)
        {
            var page = await Stale()
                .Where(filing => !failedThisRun.Contains(filing.Id))
                .OrderBy(filing => filing.FilingDate)
                .ThenBy(filing => filing.Id)
                .Select(filing => filing.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
                break;

            foreach (var filingId in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await ReprocessFiling(filingId, cancellationToken))
                        result.Processed++;
                }
                catch (HttpRequestException exception) when (IsOutage(exception))
                {
                    // Reported, not just logged: a filing that keeps failing this way stops every
                    // pass at the same row, and only the error dashboard would show it.
                    _logger.LogWarning(
                        exception,
                        "N-CEN reprocess stopped: EDGAR is unavailable ({Status})",
                        exception.StatusCode
                    );
                    await _errorReporter.Report(
                        ErrorSource.NCenReprocess,
                        "NCenReprocess.Outage",
                        $"N-CEN reprocess stopped at filing {filingId}: {exception.StatusCode?.ToString() ?? "no response"}",
                        null
                    );
                    result.StoppedByOutage = true;
                    return Finish(result);
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException
                        || !cancellationToken.IsCancellationRequested
                    )
                {
                    _dbContext.ChangeTracker.Clear();
                    await RecordFailedAttempt(filingId, exception, cancellationToken);
                    failedThisRun.Add(filingId);
                    result.Failed++;
                }
                finally
                {
                    _dbContext.ChangeTracker.Clear();
                }
            }

            _logger.LogInformation(
                "N-CEN reprocess: {Processed}/{Total} filings, {Failed} failed",
                result.Processed,
                result.Total,
                result.Failed
            );
        }

        return Finish(result);
    }

    private IQueryable<NCenFiling> Stale() =>
        _filingRepository
            .GetAll()
            .Where(filing => filing.ParserVersion < NCenFiling.CurrentParserVersion);

    private NCenFilingReprocessResult Finish(NCenFilingReprocessResult result)
    {
        if (result.Total > 0)
            _logger.LogInformation("{Summary}", result.Summary);
        return result;
    }

    // Returns false when the filing was deleted or advanced since its page was read.
    private async Task<bool> ReprocessFiling(Guid filingId, CancellationToken cancellationToken)
    {
        var filing = await _filingRepository
            .GetAll()
            .Include(f => f.Issuer)
            .FirstOrDefaultAsync(f => f.Id == filingId, cancellationToken);
        if (filing == null || filing.ParserVersion >= NCenFiling.CurrentParserVersion)
            return false;

        var cik = filing.Issuer?.Cik;
        if (string.IsNullOrWhiteSpace(cik))
            throw new InvalidDataException(
                $"N-CEN {filing.AccessionNumber} has no registrant CIK to re-fetch it with."
            );

        var content = await _secEdgarClient.GetDocumentContent(
            filing.AccessionNumber,
            cik,
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException(
                $"EDGAR returned no content for N-CEN {filing.AccessionNumber}."
            );

        var filingData = new FilingData
        {
            Cik = cik,
            AccessionNumber = filing.AccessionNumber,
            Form = filing.IsAmendment ? "N-CEN/A" : "N-CEN",
            FilingDate = filing.FilingDate,
            ReportDate = filing.ReportEndingPeriod,
        };
        var root = await EdgarXmlSubmissionParser.TryParseSubmission(
            content,
            filingData,
            filing.Issuer?.Presentation?.Listing?.Ticker,
            "N-CEN",
            "NCen.Reprocess",
            _logger,
            _errorReporter
        );
        var parsed =
            root == null
                ? null
                : NCenFilingProcessor.ParseEntity(root, filing.EquityIssuerId, filingData);
        if (parsed == null)
            throw new InvalidDataException(
                $"N-CEN {filing.AccessionNumber} has no readable registrantInfo."
            );

        filing.IsAmendment = parsed.IsAmendment;
        filing.RegistrantName = parsed.RegistrantName;
        filing.InvestmentCompanyType = parsed.InvestmentCompanyType;
        filing.InvestmentCompanyFileNumber = parsed.InvestmentCompanyFileNumber;
        filing.RegistrantLei = parsed.RegistrantLei;
        filing.State = parsed.State;
        filing.Country = parsed.Country;
        filing.ReportEndingPeriod = parsed.ReportEndingPeriod;
        filing.IsReportPeriodLessThan12Months = parsed.IsReportPeriodLessThan12Months;
        filing.IsFirstFiling = parsed.IsFirstFiling;
        filing.IsLastFiling = parsed.IsLastFiling;
        filing.IsFamilyInvestmentCompany = parsed.IsFamilyInvestmentCompany;
        filing.ParserVersion = NCenFiling.CurrentParserVersion;
        filing.ReprocessAttempts = 0;

        // Providers are added through their own set so EF inserts them; through the tracked
        // filing's collection their preset keys would read as existing rows.
        var stored = await _dbContext
            .Set<NCenServiceProvider>()
            .Where(provider => provider.NCenFilingId == filing.Id)
            .ToListAsync(cancellationToken);
        _dbContext.Set<NCenServiceProvider>().RemoveRange(stored);
        foreach (var provider in parsed.ServiceProviders)
        {
            provider.NCenFilingId = filing.Id;
            provider.NCenFiling = null;
        }
        _dbContext.Set<NCenServiceProvider>().AddRange(parsed.ServiceProviders);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    // A missing status, a server error, a throttle or a 403 (SEC's rate-limit answer) is an outage;
    // any other refusal, like a 404 for a pulled submission, is final for that filing.
    private static bool IsOutage(HttpRequestException exception) =>
        exception.StatusCode
            is null
                or >= HttpStatusCode.InternalServerError
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.Forbidden;

    // Records the attempt on a freshly loaded row; a failure while recording never ends the pass.
    private async Task RecordFailedAttempt(
        Guid filingId,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var filing = await _filingRepository
                .GetAll()
                .FirstOrDefaultAsync(f => f.Id == filingId, cancellationToken);
            if (filing == null)
                return;

            filing.ReprocessAttempts++;
            if (filing.ReprocessAttempts >= MaxReprocessAttempts)
                filing.ParserVersion = NCenFiling.CurrentParserVersion;

            _logger.LogWarning(
                exception,
                "N-CEN reprocess failed for {AccessionNumber} (attempt {Attempts}{Outcome})",
                filing.AccessionNumber,
                filing.ReprocessAttempts,
                filing.ReprocessAttempts >= MaxReprocessAttempts ? ", kept its stored facts" : ""
            );
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recordException)
            when (recordException is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogWarning(
                recordException,
                "N-CEN reprocess could not record the failed attempt for filing {FilingId}",
                filingId
            );
        }
    }
}
