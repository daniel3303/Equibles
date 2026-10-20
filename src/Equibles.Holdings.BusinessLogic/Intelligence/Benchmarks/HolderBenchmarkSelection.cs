namespace Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

/// <summary>
/// Picks one benchmark per catalog key from every provider's candidates for a quarter: the one
/// whose as-of date is nearest the quarter end, then the one on or before it, then the lower
/// priority. A key outside the catalog or with no weight is dropped.
/// </summary>
public static class HolderBenchmarkSelection
{
    public static List<HolderBenchmark> Choose(
        IEnumerable<HolderBenchmark> candidates,
        DateOnly reportDate
    ) =>
        candidates
            .Where(benchmark =>
                HolderBenchmarkCatalog.Contains(benchmark.Key)
                && benchmark.Weights.Values.Any(weight => weight > 0)
            )
            .GroupBy(benchmark => benchmark.Key)
            .Select(group =>
                group
                    .OrderBy(benchmark => Math.Abs(benchmark.AsOf.DayNumber - reportDate.DayNumber))
                    .ThenBy(benchmark => benchmark.AsOf > reportDate)
                    .ThenBy(benchmark => benchmark.Priority)
                    .ThenBy(benchmark => benchmark.Source, StringComparer.Ordinal)
                    .First()
            )
            .OrderBy(benchmark => HolderBenchmarkCatalog.Order(benchmark.Key))
            .ToList();
}
