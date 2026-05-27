using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

/// <summary>
/// Pins the new Sector taxonomy + Industry.SectorId FK applied by the
/// AddSectorTaxonomy migration. Asserts the Sector table exists, an
/// Industry can be linked to it, and the navigation resolves on read.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SectorTaxonomyTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public SectorTaxonomyTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task Sector_CanBeInsertedAndQueried()
    {
        await using var seed = FreshContext();
        var sector = new Sector { Name = "Technology" };
        seed.Add(sector);
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var found = await read.Set<Sector>().SingleAsync(s => s.Id == sector.Id);

        found.Name.Should().Be("Technology");
    }

    [Fact]
    public async Task Industry_LinkedToSector_ResolvesNavigation()
    {
        await using var seed = FreshContext();
        var sector = new Sector { Name = "Energy" };
        var industry = new Industry { Name = "Oil & Gas E&P", SectorId = sector.Id };
        seed.AddRange(sector, industry);
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var found = await read.Set<Industry>()
            .Include(i => i.Sector)
            .SingleAsync(i => i.Id == industry.Id);

        found.SectorId.Should().Be(sector.Id);
        found.Sector.Should().NotBeNull();
        found.Sector.Name.Should().Be("Energy");
    }

    [Fact]
    public async Task Industry_WithoutSector_IsAllowed()
    {
        // SectorId is nullable so legacy industry rows survive the migration without
        // requiring a backfill before the worker fills the field in.
        await using var seed = FreshContext();
        var industry = new Industry { Name = "Unclassified" };
        seed.Add(industry);
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var found = await read.Set<Industry>().SingleAsync(i => i.Id == industry.Id);

        found.SectorId.Should().BeNull();
    }
}
