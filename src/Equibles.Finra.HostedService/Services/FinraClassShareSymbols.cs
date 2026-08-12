namespace Equibles.Finra.HostedService.Services;

/// <summary>
/// FINRA spells one class share three different ways across its feeds: the daily short-volume
/// files write <c>BRK/B</c> (normalized to a dash by the file parser), the weekly OTC
/// transparency feed writes <c>BRK.B</c>, and the consolidated short-interest API writes the
/// compressed <c>BRKB</c>. Stored tickers use the dash convention, so a raw map lookup silently
/// dropped every class-share record in the two lanes that never normalized (#4369). This helper
/// owns the deterministic spelling bridge in both directions. Casing is never folded here —
/// FINRA symbol casing is identity (a lowercase suffix is a different security), so each caller
/// passes the comparer its own ticker map uses.
/// </summary>
public static class FinraClassShareSymbols
{
    /// <summary>
    /// The dotted class spelling onto the stored dash convention (<c>BRK.B</c> → <c>BRK-B</c>),
    /// case-preserving. Symbols without a dot return unchanged.
    /// </summary>
    public static string DotToDash(string symbol) =>
        symbol != null && symbol.Contains('.') ? symbol.Replace('.', '-') : symbol;

    /// <summary>
    /// Builds the supplemental compressed-spelling index for the consolidated short-interest
    /// API: every stored dash ticker contributes its dash-removed spelling (<c>BRK-B</c> →
    /// <c>BRKB</c>). A compressed spelling that is itself a stored ticker, or that two dash
    /// tickers collide on, resolves to NOTHING — the identity is then ambiguous and absent
    /// beats wrong.
    /// </summary>
    public static Dictionary<string, Guid> BuildCompressedIndex(
        IReadOnlyDictionary<string, Guid> tickerMap,
        StringComparer comparer
    )
    {
        var index = new Dictionary<string, Guid>(comparer);
        var ambiguous = new HashSet<string>(comparer);
        foreach (var (ticker, stockId) in tickerMap)
        {
            if (!ticker.Contains('-'))
                continue;
            var compressed = ticker.Replace("-", "");
            if (compressed.Length == 0 || tickerMap.ContainsKey(compressed))
                continue;
            if (!index.TryAdd(compressed, stockId))
                ambiguous.Add(compressed);
        }
        foreach (var collision in ambiguous)
            index.Remove(collision);
        return index;
    }

    /// <summary>
    /// Resolves a FINRA-spelled symbol to a stored stock: exact ticker first, then the dotted
    /// spelling mapped onto the dash convention, then the compressed index.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyDictionary<string, Guid> tickerMap,
        IReadOnlyDictionary<string, Guid> compressedIndex,
        string symbol,
        out Guid stockId
    )
    {
        stockId = default;
        if (string.IsNullOrEmpty(symbol))
            return false;
        if (tickerMap.TryGetValue(symbol, out stockId))
            return true;
        var dashed = DotToDash(symbol);
        if (!ReferenceEquals(dashed, symbol) && tickerMap.TryGetValue(dashed, out stockId))
            return true;
        return compressedIndex.TryGetValue(symbol, out stockId);
    }

    /// <summary>
    /// Every spelling FINRA may use for a stored ticker, for symbol-filtered API requests: the
    /// stored dash form plus, for class shares, the compressed and dotted forms. Requesting all
    /// three is harmless — unmatched filters return nothing and responses map back through
    /// <see cref="TryResolve"/>.
    /// </summary>
    public static IEnumerable<string> RequestSpellings(string storedTicker)
    {
        yield return storedTicker;
        if (storedTicker == null || !storedTicker.Contains('-'))
            yield break;
        yield return storedTicker.Replace("-", "");
        yield return storedTicker.Replace('-', '.');
    }
}
