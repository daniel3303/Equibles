using Equibles.Holdings.BusinessLogic.Ownership.Models;

namespace Equibles.Holdings.BusinessLogic.Ownership;

/// <summary>The source tokens every holder-list surface filters by, so each accepts the same set.</summary>
public static class StockHolderSourceFilter
{
    public const string All = "all";

    /// <summary>Every token, in the order surfaces list them.</summary>
    public static IReadOnlyList<string> Tokens { get; } =
    [All, "13f", "13dg", "13d", "13g", "insider", "fund", "nonus"];

    /// <summary>The token in its canonical form, or null when it is not one of <see cref="Tokens"/>.</summary>
    public static string Normalize(string token)
    {
        var normalized = (token ?? All).Trim().ToLowerInvariant();
        return Tokens.Contains(normalized) ? normalized : null;
    }

    public static bool Matches(StockHolderSource source, string token) =>
        token switch
        {
            All => true,
            "13f" => source == StockHolderSource.Form13F,
            "13dg" => source is StockHolderSource.Schedule13D or StockHolderSource.Schedule13G,
            "13d" => source == StockHolderSource.Schedule13D,
            "13g" => source == StockHolderSource.Schedule13G,
            "insider" => source == StockHolderSource.Insider,
            "fund" => source == StockHolderSource.Fund,
            "nonus" => source == StockHolderSource.NonUs,
            _ => false,
        };

    /// <summary>A plain name for the rows a token selects, as in "No 13F rows".</summary>
    public static string Label(string token) =>
        token switch
        {
            "13f" => "13F",
            "13dg" => "13D and 13G",
            "13d" => "13D",
            "13g" => "13G",
            "insider" => "insider",
            "fund" => "fund",
            "nonus" => "non-US",
            _ => "holder",
        };
}
