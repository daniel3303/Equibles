using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Schema contract for <see cref="AumQuarterlySnapshot"/>: ReportDate is the
/// primary key, all aggregate columns persist, and an upsert by ReportDate
/// replaces the existing row.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class AumQuarterlySnapshotRepositoryTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public AumQuarterlySnapshotRepositoryTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Q4 = new(2024, 12, 31);
    private static readonly DateOnly Q3 = new(2024, 9, 30);

    [Fact]
    public async Task SnapshotRoundTrip_AllAggregateColumnsPersist()
    {
        await using var write = FreshContext();
        var sut = new AumQuarterlySnapshotRepository(write);
        sut.Add(
            new AumQuarterlySnapshot
            {
                ReportDate = Q4,
                TotalValue = 1_500_000_000L,
                FilerCount = 1234,
                PositionCount = 5_678_910,
                StockCount = 7_890,
                FilingCount = 1500,
            }
        );
        await sut.SaveChanges();

        await using var read = FreshContext();
        var stored = await new AumQuarterlySnapshotRepository(read)
            .GetAll()
            .SingleAsync(s => s.ReportDate == Q4);

        stored.TotalValue.Should().Be(1_500_000_000L);
        stored.FilerCount.Should().Be(1234);
        stored.PositionCount.Should().Be(5_678_910);
        stored.StockCount.Should().Be(7_890);
        stored.FilingCount.Should().Be(1500);
    }

    [Fact]
    public async Task PrimaryKey_IsReportDate_OnePerQuarter()
    {
        await using var ctx1 = FreshContext();
        var sut1 = new AumQuarterlySnapshotRepository(ctx1);
        sut1.Add(new AumQuarterlySnapshot { ReportDate = Q4, TotalValue = 100L });
        sut1.Add(new AumQuarterlySnapshot { ReportDate = Q3, TotalValue = 50L });
        await sut1.SaveChanges();

        await using var read = FreshContext();
        var all = await new AumQuarterlySnapshotRepository(read)
            .GetAll()
            .OrderByDescending(s => s.ReportDate)
            .ToListAsync();

        all.Should().HaveCount(2);
        all[0].ReportDate.Should().Be(Q4);
        all[1].ReportDate.Should().Be(Q3);
    }
}
