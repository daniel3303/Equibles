using Equibles.Holdings.BusinessLogic.Intelligence;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class ActiveShareCalculatorTests
{
    [Fact]
    public void ActiveShare_ClosedFormEqualsTheUnionSum()
    {
        var random = new Random(7);
        for (var trial = 0; trial < 200; trial++)
        {
            var portfolio = RandomBook(random, 0, 60);
            var benchmark = RandomBook(random, 30, 90);

            var closedForm = ActiveShareCalculator.ActiveShare(portfolio, benchmark);
            if (portfolio.Values.Sum() == 0 || benchmark.Values.Sum() == 0)
            {
                closedForm.Should().BeNull();
                continue;
            }

            closedForm.Should().BeApproximately(BruteForce(portfolio, benchmark), 0.000000001m);
        }
    }

    [Fact]
    public void Measure_ClosedFormEqualsTheUnionSum_AndOverlapIsTheSharedBenchmarkWeight()
    {
        var random = new Random(11);
        for (var trial = 0; trial < 200; trial++)
        {
            var portfolio = RandomBook(random, 0, 400);
            var benchmark = RandomBook(random, 100, 600);
            if (portfolio.Values.Sum() == 0 || benchmark.Values.Sum() == 0)
                continue;

            var measured = ActiveShareCalculator.Measure(portfolio, benchmark).Value;

            measured
                .ActiveShare.Should()
                .BeApproximately(BruteForce(portfolio, benchmark), 0.000000001m);
            var benchmarkTotal = benchmark.Values.Sum();
            measured
                .Overlap.Should()
                .BeApproximately(
                    benchmark
                        .Where(pair => portfolio.GetValueOrDefault(pair.Key) > 0)
                        .Sum(pair => pair.Value / benchmarkTotal),
                    0.000000001m
                );
        }
    }

    [Fact]
    public void Measure_AnUnresolvedBenchmarkNameCountsAsANameTheBookLacks()
    {
        // The book holds the index's two resolved names; a third, unresolved, keeps its weight.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var book = new Dictionary<Guid, decimal> { [first] = 50, [second] = 50 };
        var index = new Dictionary<Guid, decimal>
        {
            [first] = 45,
            [second] = 45,
            [Guid.NewGuid()] = 10,
        };

        var measured = ActiveShareCalculator.Measure(book, index).Value;

        measured.ActiveShare.Should().Be(0.10m, "half of 0.05 + 0.05 + 0.10");
        measured.Overlap.Should().Be(0.90m);
    }

    [Fact]
    public void ActiveShare_IdenticalBooksAreZeroAndDisjointBooksAreOne()
    {
        var book = new Dictionary<int, decimal> { [1] = 3, [2] = 1 };

        ActiveShareCalculator.ActiveShare(book, book).Should().Be(0m);
        ActiveShareCalculator
            .ActiveShare(book, new Dictionary<int, decimal> { [3] = 1 })
            .Should()
            .Be(1m);
    }

    [Fact]
    public void ActiveShare_ScalesEachSideToOne()
    {
        var weights = new Dictionary<int, decimal> { [1] = 0.75m, [2] = 0.25m };
        var dollars = new Dictionary<int, decimal> { [1] = 750_000m, [2] = 250_000m };

        ActiveShareCalculator.ActiveShare(dollars, weights).Should().Be(0m);
    }

    [Fact]
    public void ActiveShare_AnEmptySideHasNoActiveShare()
    {
        var book = new Dictionary<int, decimal> { [1] = 1 };

        ActiveShareCalculator.ActiveShare(new Dictionary<int, decimal>(), book).Should().BeNull();
        ActiveShareCalculator.ActiveShare(book, new Dictionary<int, decimal>()).Should().BeNull();
    }

    private static Dictionary<int, decimal> RandomBook(Random random, int first, int last) =>
        Enumerable
            .Range(first, last - first)
            .Where(_ => random.NextDouble() < 0.6)
            .ToDictionary(key => key, _ => (decimal)random.Next(0, 1_000));

    private static decimal BruteForce(
        Dictionary<int, decimal> portfolio,
        Dictionary<int, decimal> benchmark
    )
    {
        var portfolioTotal = portfolio.Values.Sum();
        var benchmarkTotal = benchmark.Values.Sum();
        return portfolio
                .Keys.Union(benchmark.Keys)
                .Sum(key =>
                    Math.Abs(
                        portfolio.GetValueOrDefault(key) / portfolioTotal
                            - benchmark.GetValueOrDefault(key) / benchmarkTotal
                    )
                ) / 2;
    }
}
