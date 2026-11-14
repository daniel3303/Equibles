using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Runs <see cref="ChunkTickerConvergenceService"/> at the pace of
/// <see cref="ChunkTickerConvergencePacer"/>, and never chains one cycle into the next.
/// </summary>
public class ChunkTickerConvergenceWorker : BaseScraperWorker
{
    private readonly TimeProvider _timeProvider;
    private readonly ChunkTickerConvergencePacer _pacer = new();

    protected override string WorkerName => "Chunk ticker convergence";
    protected override TimeSpan SleepInterval => TimeSpan.FromMinutes(5);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentProcessor;

    // Behind the filing scraper and the EDGAR backfills, which start within 15 minutes.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(20);

    public ChunkTickerConvergenceWorker(
        ILogger<ChunkTickerConvergenceWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        TimeProvider timeProvider
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _timeProvider = timeProvider;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ChunkTickerConvergenceService>();
        var (documents, chunks) = await _pacer.RunCycle(
            service.FindStaleDocuments,
            service.Restamp,
            _timeProvider.GetUtcNow(),
            stoppingToken
        );
        if (documents > 0)
            Logger.LogInformation(
                "Chunk ticker convergence cycle: {Documents} document(s), {Chunks} chunk(s) re-stamped, {Pending} document(s) left",
                documents,
                chunks,
                _pacer.Pending
            );
    }
}
