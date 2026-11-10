namespace Equibles.Yahoo.Data.Prices;

/// <summary>One traded daily bar's range and close, in the listing's quoted unit.</summary>
public readonly record struct TradedBar(DateOnly Date, decimal Low, decimal High, decimal Close);
