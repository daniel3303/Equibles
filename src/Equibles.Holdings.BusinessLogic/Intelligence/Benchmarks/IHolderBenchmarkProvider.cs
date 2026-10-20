namespace Equibles.Holdings.BusinessLogic.Intelligence.Benchmarks;

/// <summary>
/// Supplies the benchmarks a closed quarter's 13F books are measured against. The holdings
/// worker registers the combined 13F book and index-tracking fund holdings; a deployment adds
/// other sources with <c>TryAddEnumerable</c>. Each benchmark key resolves to the candidate whose
/// as-of date is nearest the quarter end (<see cref="HolderBenchmarkSelection"/>).
/// </summary>
public interface IHolderBenchmarkProvider
{
    /// <summary>
    /// The benchmarks this source holds for the quarter ending <paramref name="reportDate"/>, none
    /// when it has nothing inside its own date window. Keys must be in
    /// <see cref="HolderBenchmarkCatalog"/>; an exception fails the quarter's rebuild, so a
    /// quarter never publishes a partial benchmark set.
    /// </summary>
    Task<IReadOnlyList<HolderBenchmark>> Load(
        DateOnly reportDate,
        CancellationToken cancellationToken
    );
}
