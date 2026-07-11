namespace Equibles.CommonStocks.Data.Helpers;

// SEC EDGAR SIC codes with product meaning. CommonStock.Sic carries the 4-digit code
// from EDGAR's submissions metadata — the authoritative classification signal.
// Company-type detection must compare against these codes exactly; never infer a
// company's type from its name, ticker, or third-party industry labels.
public static class KnownSicCodes
{
    // Real Estate Investment Trusts.
    public const string Reit = "6798";
}
