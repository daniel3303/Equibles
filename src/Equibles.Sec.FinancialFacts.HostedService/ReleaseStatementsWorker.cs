using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.FinancialFacts.HostedService;

/// <summary>
/// Reads each recent earnings release once per matcher version for the statement figures it
/// states before the 10-Q or 10-K. A release with no reading at the current version qualifies,
/// so raising <see cref="Data.Models.ReleaseStatementRead.CurrentMatcherVersion"/> re-reads the
/// window.
/// </summary>
public class ReleaseStatementsWorker : BaseScraperWorker
{
    private readonly ReleaseStatementsOptions _options;

    // Consecutive failures that end a cycle: the cause is then the database or the file store,
    // not the releases, and the rest of the batch is left untouched for the next cycle.
    private const int OutageFailures = 3;

    private readonly ReleaseReadBackoff _backoff = new();

    protected override string WorkerName => "Earnings release statements";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinancialFactsScraper;

    // Let the filing sweeps land the day's documents first after a deploy.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(5);

    public ReleaseStatementsWorker(
        ILogger<ReleaseStatementsWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<ReleaseStatementsOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        SleepInterval = TimeSpan.FromMinutes(Math.Max(1, _options.SleepIntervalMinutes));
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, _options.BatchSize);
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-_options.LookbackDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var documents = scope.ServiceProvider.GetRequiredService<DocumentRepository>();
            var reads = scope.ServiceProvider.GetRequiredService<ReleaseStatementReadRepository>();
            var reader = scope.ServiceProvider.GetRequiredService<ReleaseStatementReadService>();

            var waiting = _backoff.Waiting(DateTime.UtcNow);
            var batch = await Unread(documents, reads, since)
                .Where(release => !waiting.Contains(release.Id))
                .OrderByDescending(release => release.ReportingDate)
                .Take(batchSize)
                .ToListAsync(stoppingToken);
            if (batch.Count == 0)
                return;

            var figures = 0;
            var failuresInARow = 0;
            foreach (var release in batch)
            {
                stoppingToken.ThrowIfCancellationRequested();
                try
                {
                    figures += await reader.Read(release, stoppingToken);
                    _backoff.Succeeded(release.Id);
                    failuresInARow = 0;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _backoff.Failed(release.Id, DateTime.UtcNow);
                    reads.ClearChangeTracker();
                    Logger.LogWarning(
                        ex,
                        "Earnings release statements read failed for document {DocumentId} ({Accession}).",
                        release.Id,
                        release.AccessionNumber
                    );
                    await ErrorReporter.Report(
                        ErrorSource,
                        "ReleaseStatements.Read",
                        ex,
                        $"documentId: {release.Id}, accession: {release.AccessionNumber}"
                    );
                    if (++failuresInARow >= OutageFailures)
                        return;
                }
            }

            Logger.LogInformation(
                "Earnings release statements cycle: {Releases} release(s) read, {Figures} figure(s) placed.",
                batch.Count,
                figures
            );

            if (batch.Count < batchSize)
                return;
        }
    }

    /// <summary>Stored earnings releases filed since the date with no reading at the current version.</summary>
    internal static IQueryable<Document> Unread(
        DocumentRepository documents,
        ReleaseStatementReadRepository reads,
        DateOnly since
    )
    {
        var current = reads.GetCurrent();
        return documents
            .GetEarningsReleasesSince(since)
            .Where(release =>
                release.LineCount > 0 && !current.Any(read => read.DocumentId == release.Id)
            );
    }
}
