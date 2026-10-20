namespace Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

/// <summary>
/// The benchmarks a 13F book is measured against, keyed by the stable key stored on every
/// <c>HolderQuarterlyBenchmarkActiveShare</c> row. Providers supply weights for these keys only,
/// so two sources of one index compete for one row and every surface names it once.
/// </summary>
public static class HolderBenchmarkCatalog
{
    public const string Aggregate = "13f-aggregate";
    public const string Sp500 = "sp-500";
    public const string Sp400 = "sp-400";
    public const string Sp600 = "sp-600";
    public const string Russell1000 = "russell-1000";
    public const string Russell2000 = "russell-2000";
    public const string Nasdaq100 = "nasdaq-100";
    public const string DowJones = "dow-jones";

    /// <summary>Every benchmark in the order surfaces list them, the published indexes first.</summary>
    public static IReadOnlyList<(string Key, string Name)> All { get; } =
    [
        (Sp500, "S&P 500"),
        (Sp400, "S&P MidCap 400"),
        (Sp600, "S&P SmallCap 600"),
        (Russell1000, "Russell 1000"),
        (Russell2000, "Russell 2000"),
        (Nasdaq100, "Nasdaq-100"),
        (DowJones, "Dow Jones Industrial Average"),
        (Aggregate, HolderStyleDescriptions.AggregateBenchmark),
    ];

    private static readonly Dictionary<string, (int Order, string Name)> ByKey = All.Select(
            (entry, order) => (entry.Key, Order: order, entry.Name)
        )
        .ToDictionary(entry => entry.Key, entry => (entry.Order, entry.Name));

    public static bool Contains(string key) => key != null && ByKey.ContainsKey(key);

    /// <summary>The benchmark's display name; null for a key outside the catalog.</summary>
    public static string Name(string key) =>
        key != null && ByKey.TryGetValue(key, out var entry) ? entry.Name : null;

    /// <summary>The benchmark's position in <see cref="All"/>; unknown keys sort last.</summary>
    public static int Order(string key) =>
        key != null && ByKey.TryGetValue(key, out var entry) ? entry.Order : int.MaxValue;
}
