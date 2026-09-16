using Equibles.Integrations.DelayedTrades;

namespace Equibles.DelayedTrades.BusinessLogic.Prints;

// First pass over a file: which trade identifiers were cancelled and which amendment row is the last word.
public sealed class DelayedTradeModificationLedger
{
    private readonly HashSet<string> _cancelled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _amendedLine = new(StringComparer.Ordinal);

    public int CancelledCount => _cancelled.Count;
    public int AmendedCount => _amendedLine.Count;

    public static DelayedTradeModificationLedger Build(IEnumerable<DelayedTradePrint> prints)
    {
        var ledger = new DelayedTradeModificationLedger();
        foreach (var print in prints)
        {
            if (string.IsNullOrEmpty(print.TradeId))
                continue;
            switch (print.Modification)
            {
                case DelayedTradeModification.Cancellation:
                    ledger._cancelled.Add(print.TradeId);
                    break;
                case DelayedTradeModification.Amendment:
                    ledger._amendedLine[print.TradeId] = Math.Max(
                        print.LineNumber,
                        ledger._amendedLine.GetValueOrDefault(print.TradeId)
                    );
                    break;
            }
        }
        return ledger;
    }

    // A cancelled identifier drops every row; an amended one keeps only its latest amendment row.
    public bool IsEffective(DelayedTradePrint print)
    {
        if (print.Modification == DelayedTradeModification.Cancellation)
            return false;
        if (string.IsNullOrEmpty(print.TradeId))
            return print.Modification == DelayedTradeModification.None;
        if (_cancelled.Contains(print.TradeId))
            return false;
        return !_amendedLine.TryGetValue(print.TradeId, out var line) || print.LineNumber == line;
    }
}
