using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Data.Models.Taxonomies;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Consumers;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Holdings;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: consuming a <see cref="Filings13FImported"/> event rebuilds the
/// per-quarter AUM snapshot row for the message's <c>ReportDate</c>.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Filings13FImportedConsumerTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public Filings13FImportedConsumerTests(ParadeDbFixture fixture) => _fixture = fixture;

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

    private HoldingsAggregateRefreshService BuildRefreshService()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => CreateScopeFromFixture());
        return new HoldingsAggregateRefreshService(
            scopeFactory,
            NullLogger<HoldingsAggregateRefreshService>.Instance
        );
    }

    private IServiceScope CreateScopeFromFixture()
    {
        var ctx = FreshContext();
        var scope = Substitute.For<IServiceScope>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(Equibles.Data.EquiblesFinancialDbContext)).Returns(ctx);
        provider
            .GetService(typeof(AumQuarterlySnapshotRepository))
            .Returns(_ => new AumQuarterlySnapshotRepository(ctx));
        provider
            .GetService(typeof(SectorQuarterlySnapshotRepository))
            .Returns(_ => new SectorQuarterlySnapshotRepository(ctx));
        scope.ServiceProvider.Returns(provider);
        return scope;
    }

    private static ConsumeContext<Filings13FImported> Context(Filings13FImported message)
    {
        var ctx = Substitute.For<ConsumeContext<Filings13FImported>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_PublishedQuarter_WritesSnapshotRowForThatQuarter()
    {
        // Seed two holdings in Q4 — one filer, two stocks, one accession.
        await using (var seed = FreshContext())
        {
            var tech = new Sector { Name = "Technology" };
            seed.Add(tech);
            await seed.SaveChangesAsync();
            var industry = new Industry { Name = "Software", SectorId = tech.Id };
            seed.Add(industry);
            await seed.SaveChangesAsync();
            var aapl = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple",
                Cik = "C0000320193",
                IndustryId = industry.Id,
            };
            var msft = new CommonStock
            {
                Ticker = "MSFT",
                Name = "Microsoft",
                Cik = "C0000789019",
                IndustryId = industry.Id,
            };
            seed.AddRange(aapl, msft);
            var holder = new InstitutionalHolder { Cik = "H001", Name = "Holder H001" };
            seed.Add(holder);
            await seed.SaveChangesAsync();
            seed.AddRange(
                MakeHolding(aapl, holder, Q4, 100_000, "acc-q4"),
                MakeHolding(msft, holder, Q4, 200_000, "acc-q4")
            );
            await seed.SaveChangesAsync();
        }

        var sut = new Filings13FImportedConsumer(
            BuildRefreshService(),
            NullLogger<Filings13FImportedConsumer>.Instance
        );

        await sut.Consume(Context(new Filings13FImported(Q4, FilingCount: 1)));

        await using var read = FreshContext();
        var aum = await read.Set<AumQuarterlySnapshot>().SingleAsync(s => s.ReportDate == Q4);
        aum.TotalValue.Should().Be(300_000);
        aum.FilerCount.Should().Be(1);
        aum.PositionCount.Should().Be(2);
        aum.FilingCount.Should().Be(1);
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long value,
        string accession
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
            Cusip =
                $"{stock.Ticker[..Math.Min(4, stock.Ticker.Length)]}{stock.Id.GetHashCode():X8}"[
                    ..9
                ],
        };
}
