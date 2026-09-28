using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Messaging.Attributes;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;

namespace Equibles.Holdings.HostedService.Consumers;

/// <summary>Retains the source identities to discover missing positions without resetting the bulk ledger.</summary>
[Consumer]
public class StockCusipChangedConsumer(
    HoldingsCusipRescanRepository requests,
    HoldingsRescanSignal signal,
    ILogger<StockCusipChangedConsumer> logger
) : IConsumer<StockCusipChanged>
{
    public async Task Consume(ConsumeContext<StockCusipChanged> context)
    {
        var message = context.Message;
        if (
            string.IsNullOrWhiteSpace(message.Cusip)
            && string.IsNullOrWhiteSpace(message.PreviousCusip)
        )
            return;
        await requests.Enqueue(
            new HoldingsCusipRescan
            {
                Id = context.MessageId ?? Guid.NewGuid(),
                EquityIssuerId = message.CommonStockId,
                Ticker = message.Ticker,
                PreviousCusip = message.PreviousCusip,
                Cusip = message.Cusip,
            },
            context.CancellationToken
        );
        signal.RequestRescan();
        logger.LogInformation(
            "Queued source-based 13F rescan for {Ticker}: {PreviousCusip} -> {Cusip}",
            message.Ticker,
            message.PreviousCusip,
            message.Cusip
        );
    }
}
