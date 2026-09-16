namespace Equibles.Integrations.Lse;

// The markets the instrument list names, each mapped to the venue code FIRDS and the catalog use. A market
// outside this table is not guessed at: the line is skipped, and a new one is a deliberate change here.
public static class LseMarketSegments
{
    private static readonly IReadOnlyDictionary<string, string> Segments = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["MAIN MARKET"] = "XLON",
        ["MAIN MARKET - SFS"] = "XLON",
        ["ADMISSION TO TRADING ONLY"] = "XLON",
        ["AIM"] = "AIMX",
    };

    public static string TryResolve(string lseMarket) =>
        lseMarket != null && Segments.TryGetValue(lseMarket.Trim(), out var mic) ? mic : null;
}
