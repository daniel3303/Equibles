using Equibles.CommonStocks.HostedService.Services;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Equibles.CommonStocks.HostedService.Consumers;

/// <summary>
/// When website discovery fills a stock's website, probe that stock for an IR page immediately off
/// the new website — so a fresh website cascades straight into IR discovery instead of waiting out
/// that worker's own cooldown (the cascade <see cref="WebsiteDiscoveryService"/> publishes for).
/// Idempotent: <see cref="InvestorRelationsDiscoveryService.DiscoverForStock"/> no-ops when the stock
/// has no website or already has an IR page, so a duplicate event is harmless.
/// </summary>
[Consumer]
public class StockWebsiteDiscoveredConsumer : IConsumer<StockWebsiteDiscovered>
{
    private readonly InvestorRelationsDiscoveryService _discovery;
    private readonly ILogger<StockWebsiteDiscoveredConsumer> _logger;

    public StockWebsiteDiscoveredConsumer(
        InvestorRelationsDiscoveryService discovery,
        ILogger<StockWebsiteDiscoveredConsumer> logger
    )
    {
        _discovery = discovery;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockWebsiteDiscovered> context)
    {
        var found = await _discovery.DiscoverForStock(
            context.Message.CommonStockId,
            context.CancellationToken
        );

        _logger.LogInformation(
            "Website discovered for {Ticker} ({Website}); IR probe {Outcome}",
            context.Message.Ticker,
            context.Message.Website,
            found ? "found an IR page" : "found none (or one already existed)"
        );
    }
}
