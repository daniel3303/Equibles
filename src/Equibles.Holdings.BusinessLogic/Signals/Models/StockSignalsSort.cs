namespace Equibles.Holdings.BusinessLogic.Signals.Models;

/// <summary>Market-wide orderings of the stock holder signals.</summary>
public enum StockSignalsSort
{
    /// <summary>Filers with the stock among their ten largest holdings.</summary>
    Conviction,

    /// <summary>Shares held by high-turnover filers and private-fund advisers over shares outstanding.</summary>
    Crowding,
}
