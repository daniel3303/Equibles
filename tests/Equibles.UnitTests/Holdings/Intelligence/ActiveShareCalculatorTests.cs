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

            closedForm.Should().BeApproximately(BruteForce(portfolio, benchmark), 0.000000001m);
        }
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
