using BenchmarkDotNet.Running;

namespace Equibles.Benchmarks;

public static class Program {
    // Pass --list flat / --filter "*HoldingsDataSet*" / --filter "*" to BenchmarkSwitcher.
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
