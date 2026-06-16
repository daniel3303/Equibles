using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Sweeps EDGAR's daily index for freshly accepted NPORT-P portfolio reports across all filers and
/// ingests the ones filed by fund-family trusts that are not tracked stocks — the source of the
/// "held by funds" coverage gap, where the largest index funds (Vanguard, Fidelity, iShares) never
/// appeared because their multi-series trusts are not crawled through the issuer feed.
///
/// The full trailing window is re-swept each cycle and progress is deduped by accession number
/// (against both stored and skip-recorded filings), so no watermark is needed and a transient
/// daily-index failure self-heals next cycle. Runs in the worker process so it shares the single SEC
/// rate-limiter with the other EDGAR scrapers, and staggers past deploy so its initial burst doesn't
/// crowd the time-sensitive scrapers.
/// </summary>
public class NportRealtimeWorker : BaseScraperWorker
{
    // The fund universe files NPORT-P monthly but discloses quarterly, with up to 60 days after a
    // quarter end to file — so the window must span a full filing cluster. Re-sweeping these days is
    // cheap (one small index file each); only genuinely new submissions are downloaded.
    private const int DefaultLookbackDays = 70;

    // Caps submissions downloaded per cycle so a cold-start backlog drains in bursts through the
    // shared SEC budget instead of one long run that starves the other scrapers.
    private const int DefaultMaxFetchesPerCycle = 500;

    private readonly IConfiguration _configuration;

    protected override string WorkerName => "NPORT-P real-time ingestion";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.NportSweep;

    // Stagger past deploy so the initial daily-index burst doesn't collide with the other SEC
    // scrapers (and after the reprocess worker, which staggers by 5 minutes).
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(7);

    public NportRealtimeWorker(
        ILogger<NportRealtimeWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (!_configuration.GetValue("NportSweep:Enabled", true))
        {
            Logger.LogInformation(
                "NPORT-P real-time ingestion disabled (NportSweep:Enabled=false)"
            );
            return false;
        }

        return ValidateSecContactEmail(
            _configuration,
            "NPORT-P real-time ingestion",
            treatWhitespaceAsAbsent: true
        );
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var lookbackDays = Math.Max(
            1,
            _configuration.GetValue("NportSweep:LookbackDays", DefaultLookbackDays)
        );
        var maxFetches = Math.Max(
            1,
            _configuration.GetValue("NportSweep:MaxFetchesPerCycle", DefaultMaxFetchesPerCycle)
        );
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<NportRealtimeIngestionService>();

        var result = await service.IngestRecentFilings(
            today,
            lookbackDays,
            maxFetches,
            stoppingToken
        );

        // Cold start before the tracked-stock universe is populated — back off briefly and retry,
        // don't sleep the full interval.
        if (result.NotReady)
        {
            RequestRetrySoon();
            return;
        }

        // A capped cycle left candidates behind — drain them promptly rather than waiting the full
        // interval, while a brief gap still yields the shared SEC budget to the other scrapers.
        if (result.MoreWorkQueued)
            RequestImmediateContinuation();
    }
}
