using Equibles.Holdings.HostedService.Services;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.Holdings;
using MassTransit;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>
/// Rebuilds the per-quarter AUM and sector-allocation snapshots for the
/// affected ReportDate after a successful 13F import. Idempotent — re-running
/// for the same quarter just recomputes the same row. The daily safety-net
/// worker is the second line of defence if a message is lost to a transient
/// bus failure.
/// </summary>
[Consumer]
public class Filings13FImportedConsumer : IConsumer<Filings13FImported>
{
    private readonly HoldingsAggregateRefreshService _refreshService;
    private readonly ILogger<Filings13FImportedConsumer> _logger;

    public Filings13FImportedConsumer(
        HoldingsAggregateRefreshService refreshService,
        ILogger<Filings13FImportedConsumer> logger
    )
    {
        _refreshService = refreshService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Filings13FImported> context)
    {
        var reportDate = context.Message.ReportDate;

        _logger.LogInformation(
            "Rebuilding holdings aggregate snapshots for {ReportDate} ({FilingCount} filing(s))",
            reportDate,
            context.Message.FilingCount
        );

        await _refreshService.RebuildQuarterAsync(reportDate, context.CancellationToken);
    }
}
