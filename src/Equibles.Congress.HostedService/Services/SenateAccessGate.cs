using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Holds Senate eFD fetches for a day after eFD denies this host, so a lasting block costs one
/// Errors row a day instead of one per lane per worker start. The denial's Errors row is the
/// record: the outage stays visible, and deleting the row lets the next cycle try again.
/// </summary>
[Service]
public class SenateAccessGate
{
    internal const string ErrorContext = "Congress.SenateAccessDenied";
    internal static readonly TimeSpan HoldInterval = TimeSpan.FromDays(1);

    private readonly ErrorRepository _errorRepository;
    private readonly ILogger<SenateAccessGate> _logger;

    public SenateAccessGate(ErrorRepository errorRepository, ILogger<SenateAccessGate> logger)
    {
        _errorRepository = errorRepository;
        _logger = logger;
    }

    // Fails open: a lookup fault attempts the fetch rather than hiding a source that recovered.
    public virtual async Task ThrowIfHeld(CancellationToken ct)
    {
        DateTime? deniedAt;
        try
        {
            var since = DateTime.UtcNow - HoldInterval;
            deniedAt = await _errorRepository
                .GetBySource(ErrorSource.CongressScraper)
                .Where(error => error.Context == ErrorContext && error.CreationTime > since)
                .MaxAsync(error => (DateTime?)error.CreationTime, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the last Senate eFD denial; fetching anyway");
            return;
        }

        if (deniedAt is { } at)
            throw new SenateAccessDeniedException(
                $"Senate eFD denied access at {at:u}; the next attempt is after "
                    + $"{at + HoldInterval:u}",
                observed: false
            );
    }

    /// <summary>
    /// Records a denial eFD answered as one Errors row, which holds both Senate lanes; a held
    /// attempt is only logged.
    /// </summary>
    internal static async Task Record(
        SenateAccessDeniedException denial,
        ErrorReporter errorReporter,
        ILogger logger,
        string lane
    )
    {
        if (!denial.Observed)
        {
            logger.LogInformation("{Lane} skipped: {Reason}", lane, denial.Message);
            return;
        }

        logger.LogWarning("{Lane} failed: {Reason}", lane, denial.Message);
        await errorReporter.Report(ErrorSource.CongressScraper, ErrorContext, denial);
    }
}
