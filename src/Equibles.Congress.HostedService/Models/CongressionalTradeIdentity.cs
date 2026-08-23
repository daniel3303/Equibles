using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

internal sealed record CongressionalTradeIdentity(
    Guid CommonStockId,
    DateOnly TransactionDate,
    CongressTransactionType TransactionType,
    string AssetName,
    string OwnerType,
    long AmountFrom,
    long AmountTo,
    string AssetType,
    string Subholding
)
{
    public static CongressionalTradeIdentity From(CongressionalTrade trade) =>
        new(
            trade.CommonStockId,
            trade.TransactionDate,
            trade.TransactionType,
            trade.AssetName,
            trade.OwnerType,
            trade.AmountFrom,
            trade.AmountTo,
            trade.AssetType,
            trade.Subholding
        );
}
