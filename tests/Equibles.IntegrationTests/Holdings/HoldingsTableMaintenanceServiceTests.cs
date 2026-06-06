using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// VACUUM (ANALYZE) on InstitutionalHolding runs against a real Postgres connection with no
/// ambient transaction (VACUUM is forbidden inside a transaction block) and leaves the table
/// data intact. Pins that the maintenance command is well-formed and completes — the actual risk,
/// since EF normally executes inside a transaction. Uses ParadeDB (not the in-memory factory):
/// VACUUM is Postgres-specific.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsTableMaintenanceServiceTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsTableMaintenanceServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task VacuumInstitutionalHoldings_CompletesAndLeavesDataIntact()
    {
        await SeedTwoHoldingsAsync();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = _fixture.CreateDbContext();
                _contexts.Add(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var service = new HoldingsTableMaintenanceService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsTableMaintenanceService>>()
        );

        // The command would throw if it ran inside a transaction (Postgres: "VACUUM cannot run
        // inside a transaction block") or if the SQL were malformed.
        await service.VacuumInstitutionalHoldings(CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var count = await verify.Set<InstitutionalHolding>().CountAsync(CancellationToken.None);
        count.Should().Be(2);
    }

    private async Task SeedTwoHoldingsAsync()
    {
        await using var ctx = _fixture.CreateDbContext();

        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "VACU",
            Name = "Vacuum Test Co.",
            Cik = "0009980001",
            Cusip = "099800015",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0001000020",
            Name = "Vacuum Filer",
        };
        ctx.Set<CommonStock>().Add(stock);
        ctx.Set<InstitutionalHolder>().Add(holder);
        ctx.Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, new DateOnly(2024, 9, 30), 100, "VAC-Q3"),
                Holding(stock.Id, holder.Id, new DateOnly(2024, 12, 31), 150, "VAC-Q4")
            );
        await ctx.SaveChangesAsync(CancellationToken.None);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            AccessionNumber = accession,
        };
}
