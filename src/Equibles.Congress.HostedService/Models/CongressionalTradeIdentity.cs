using Equibles.Congress.Data.Models;

namespace Equibles.Congress.HostedService.Models;

internal abstract record CongressionalTradeIdentity
{
    public static CongressionalTradeIdentity From(CongressionalTrade trade) =>
        trade.FilingKind.HasValue && trade.SourceId != null && trade.SourceRowIndex.HasValue
            ? new SourceIdentity(trade.FilingKind.Value, trade.SourceId, trade.SourceRowIndex.Value)
            : new LegacyIdentity(
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

    private sealed record SourceIdentity(
        CongressionalFilingKind FilingKind,
        string SourceId,
        int SourceRowIndex
    ) : CongressionalTradeIdentity;

    private sealed record LegacyIdentity(
        Guid? CommonStockId,
        DateOnly TransactionDate,
        CongressTransactionType TransactionType,
        string AssetName,
        string OwnerType,
        long AmountFrom,
        long AmountTo,
        string AssetType,
        string Subholding
    ) : CongressionalTradeIdentity;
}
