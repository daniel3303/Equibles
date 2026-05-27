using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Schema contract for <see cref="SectorQuarterlySnapshot"/>: composite key
/// (ReportDate, SectorId) allows one row per sector per quarter, all columns
/// persist, and rows are independently addressable by the composite key.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SectorQuarterlySnapshotRepositoryTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public SectorQuarterlySnapshotRepositoryTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task CompositeKey_AllowsMultipleSectorsPerQuarter()
    {
        var tech = Guid.NewGuid();
        var energy = Guid.NewGuid();

        await using var write = FreshContext();
        var sut = new SectorQuarterlySnapshotRepository(write);
        sut.Add(
            new SectorQuarterlySnapshot
            {
                ReportDate = Q4,
                SectorId = tech,
                SectorName = "Technology",
                TotalValue = 900_000_000L,
            }
        );
        sut.Add(
            new SectorQuarterlySnapshot
            {
                ReportDate = Q4,
                SectorId = energy,
                SectorName = "Energy",
                TotalValue = 200_000_000L,
            }
        );
        await sut.SaveChanges();

        await using var read = FreshContext();
        var rows = await new SectorQuarterlySnapshotRepository(read)
            .GetAll()
            .Where(s => s.ReportDate == Q4)
            .OrderByDescending(s => s.TotalValue)
            .ToListAsync();

        rows.Should().HaveCount(2);
        rows[0].SectorName.Should().Be("Technology");
        rows[0].TotalValue.Should().Be(900_000_000L);
        rows[1].SectorName.Should().Be("Energy");
    }
}
