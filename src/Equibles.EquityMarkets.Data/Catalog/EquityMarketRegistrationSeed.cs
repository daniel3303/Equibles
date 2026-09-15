namespace Equibles.EquityMarkets.Data.Catalog;

// The Lisbon lane predates the registration table and was switched on by a host setting; that setting
// seeds its row once so an upgrade never silences a market that was already priced.
public static class EquityMarketRegistrationSeed
{
    public const string LisbonSetting = "EquityMarkets:LisbonEnabled";
    public const string LisbonCode = "euronext-lisbon";

    public static IReadOnlySet<string> InitiallyEnabled(bool lisbonLaneEnabled) =>
        lisbonLaneEnabled
            ? new HashSet<string>(StringComparer.Ordinal) { LisbonCode }
            : new HashSet<string>(StringComparer.Ordinal);
}
