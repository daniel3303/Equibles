using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the FilingType restamp on pre-column filing rollup rows: the migration defaults every
/// row to Form13F, mislabelling the Schedule 13D/G rollups whose fresh event dates would
/// otherwise pass for a filer's newest 13F quarter in recency-ranked resolution. The truth is
/// copied from the holdings rows — never inferred from date shape — and the sweep is a no-op
/// once drained.
/// </summary>
public class FilingRollupTypeBackfillServiceTests : IDisposable
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
        new CorporateActionsModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ILogger<FilingRollupTypeBackfillService> _logger = Substitute.For<
        ILogger<FilingRollupTypeBackfillService>
    >();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        var ctx = new EquiblesFinancialDbContext(options, Modules);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = CreateSharedContext();

                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);

                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return scopeFactory;
    }

    private FilingRollupTypeBackfillService CreateService() => new(CreateScopeFactory(), _logger);

    private async Task Seed(string accession, FilingType rollupType, FilingType holdingType)
    {
        var seedContext = CreateSharedContext();
        var holderId = Guid.NewGuid();
        seedContext
            .Set<InstitutionalHolder>()
            .Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = Guid.NewGuid().ToString()[..10],
                    Name = "Filer",
                }
            );
        seedContext
            .Set<InstitutionalFiling>()
            .Add(
                new InstitutionalFiling
                {
                    AccessionNumber = accession,
                    InstitutionalHolderId = holderId,
                    FilingDate = new DateOnly(2026, 7, 23),
                    ReportDate = new DateOnly(2026, 7, 23),
                    FilingType = rollupType,
                    PositionCount = 1,
                    TotalValue = 45_000_000L,
                }
            );
        seedContext
            .Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    CommonStockId = Guid.NewGuid(),
                    InstitutionalHolderId = holderId,
                    FilingDate = new DateOnly(2026, 7, 23),
                    ReportDate = new DateOnly(2026, 7, 23),
                    FilingType = holdingType,
                    Shares = 1_000,
                    Value = 45_000_000L,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = accession,
                }
            );
        await seedContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Backfill_MislabelledScheduleRollup_IsRestampedFromItsHoldings()
    {
        await Seed("acc-13g", rollupType: FilingType.Form13F, holdingType: FilingType.Schedule13G);
        await Seed("acc-13f", rollupType: FilingType.Form13F, holdingType: FilingType.Form13F);

        var restamped = await CreateService().Backfill(CancellationToken.None);

        restamped.Should().Be(1);
        var verify = CreateSharedContext();
        (await verify.Set<InstitutionalFiling>().SingleAsync(f => f.AccessionNumber == "acc-13g"))
            .FilingType.Should()
            .Be(FilingType.Schedule13G);
        (await verify.Set<InstitutionalFiling>().SingleAsync(f => f.AccessionNumber == "acc-13f"))
            .FilingType.Should()
            .Be(FilingType.Form13F, "a genuine 13F rollup row must not be touched");
    }

    [Fact]
    public async Task Backfill_SecondPass_IsANoOp()
    {
        await Seed("acc-13d", rollupType: FilingType.Form13F, holdingType: FilingType.Schedule13D);

        (await CreateService().Backfill(CancellationToken.None)).Should().Be(1);
        (await CreateService().Backfill(CancellationToken.None)).Should().Be(0);
    }
}
