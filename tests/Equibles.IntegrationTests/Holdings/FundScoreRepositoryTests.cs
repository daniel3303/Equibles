using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Holdings;

public class FundScoreRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FundScoreRepository _repository;

    public FundScoreRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(new HoldingsModuleConfiguration());
        _repository = new FundScoreRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetRankedByAlpha_OrdersDescendingAndFiltersWindowAndBenchmark()
    {
        var lowAlpha = MakeScore(alpha: 1.5m);
        var highAlpha = MakeScore(alpha: 9.0m);
        var otherWindow = MakeScore(alpha: 99.0m, windowYears: 5);
        var otherBenchmark = MakeScore(alpha: 88.0m, benchmark: "QQQ");
        _repository.Add(lowAlpha);
        _repository.Add(highAlpha);
        _repository.Add(otherWindow);
        _repository.Add(otherBenchmark);
        await _repository.SaveChanges();

        var ranked = _repository.GetRankedByAlpha(windowYears: 3, benchmarkTicker: "SPY").ToList();

        ranked.Select(s => s.Id).Should().Equal(highAlpha.Id, lowAlpha.Id);
    }

    [Fact]
    public async Task GetByHolder_WithWindowAndBenchmark_ReturnsMatchingScore()
    {
        var holder = new InstitutionalHolder { Cik = "0001234567", Name = "Test Capital" };
        var match = MakeScore(alpha: 4.2m);
        match.InstitutionalHolderId = holder.Id;
        var differentBenchmark = MakeScore(alpha: 5.0m, benchmark: "QQQ");
        differentBenchmark.InstitutionalHolderId = holder.Id;
        _repository.Add(match);
        _repository.Add(differentBenchmark);
        await _repository.SaveChanges();

        var result = await _repository.GetByHolder(holder, windowYears: 3, benchmarkTicker: "SPY");

        result.Should().NotBeNull();
        result.Id.Should().Be(match.Id);
        result.AlphaPercent.Should().Be(4.2m);
    }

    private static FundScore MakeScore(
        decimal alpha,
        int windowYears = 3,
        string benchmark = "SPY"
    ) =>
        new()
        {
            InstitutionalHolderId = Guid.NewGuid(),
            BenchmarkTicker = benchmark,
            WindowYears = windowYears,
            WindowStart = new DateOnly(2023, 1, 1),
            WindowEnd = new DateOnly(2026, 1, 1),
            PortfolioCagrPercent = alpha + 8m,
            BenchmarkCagrPercent = 8m,
            AlphaPercent = alpha,
        };
}
