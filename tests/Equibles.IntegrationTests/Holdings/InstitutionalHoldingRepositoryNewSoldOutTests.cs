using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins <c>InstitutionalHoldingRepository.GetQuarterlyNewSoldOutPositions</c>. The query
/// uses two NOT-EXISTS subqueries against the same table and MUST translate server-side
/// — the in-memory provider does not exercise the same SQL path, so this fixture runs
/// against ParadeDB. A missing position counts as an exit only for a filer that reported a 13F
/// for the current quarter.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryNewSoldOutTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryNewSoldOutTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

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

    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_HolderAppearsOnlyInCurrent_CountsAsNew()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "AAPL");
        var holder = await SeedHolder(seed, "1");
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_000));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(1);
        row.SoldOutFilerCount.Should().Be(0);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_HolderThatLeftTheStock_CountsAsSoldOut()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "MSFT");
        EquityIssuer other = await SeedStock(seed, "KO");
        var holder = await SeedHolder(seed, "2");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000));
        seed.Add(MakeHolding(other, holder, Current, shares: 10));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(0);
        row.SoldOutFilerCount.Should().Be(1);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_HolderInBothQuarters_CountsAsNeither()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "NVDA");
        var holder = await SeedHolder(seed, "3");
        seed.Add(MakeHolding(stock, holder, Prior, shares: 1_000));
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_500));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(0);
        row.SoldOutFilerCount.Should().Be(0);
    }

    // Shape of a confidential-treatment original (Norges Bank, 2026-03-31): the filer published a
    // full book the quarter before, but its 13F for this quarter is one placeholder row, so the
    // quarter holds no row and no rollup for it.
    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_QuarterWithoutAPublished13F_IsNoSale()
    {
        var before = new DateOnly(2025, 12, 31);
        var confidential = new DateOnly(2026, 3, 31);
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "AAPL");
        var withheld = await SeedHolder(seed, "1374170");
        var seller = await SeedHolder(seed, "8");
        seed.Add(MakeHolding(stock, withheld, before, shares: 1_000));
        seed.Add(MakeHolding(stock, seller, before, shares: 500));
        seed.Add(
            new InstitutionalFiling
            {
                AccessionNumber = "seller-zero-restatement",
                InstitutionalHolderId = seller.Id,
                FilingDate = confidential.AddDays(45),
                ReportDate = confidential,
                IsAmendment = true,
                FilingType = FilingType.Form13F,
                PositionCount = 0,
                TotalValue = 0,
                DeclaredPositionCount = 0,
                DeclaredTotalValue = 0,
            }
        );
        await seed.SaveChangesAsync();

        var row = await Churn(stock, confidential, before);

        row.SoldOutFilerCount.Should()
            .Be(
                1,
                "only the filer whose restatement emptied its book sold; the withheld book did not"
            );
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_MixedFilersPerStock_CountsBothBuckets()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "TSLA");
        EquityIssuer other = await SeedStock(seed, "KO");
        var newcomer1 = await SeedHolder(seed, "4");
        var newcomer2 = await SeedHolder(seed, "5");
        var exiter = await SeedHolder(seed, "6");
        var steady = await SeedHolder(seed, "7");
        var lapsed = await SeedHolder(seed, "10");
        // Prior: exiter + steady + lapsed (which files no current 13F).
        seed.Add(MakeHolding(stock, exiter, Prior, shares: 100));
        seed.Add(MakeHolding(stock, steady, Prior, shares: 200));
        seed.Add(MakeHolding(stock, lapsed, Prior, shares: 300));
        // Current: newcomer1 + newcomer2 + steady; the exiter still files, without this stock.
        seed.Add(MakeHolding(stock, newcomer1, Current, shares: 500));
        seed.Add(MakeHolding(stock, newcomer2, Current, shares: 600));
        seed.Add(MakeHolding(stock, steady, Current, shares: 200));
        seed.Add(MakeHolding(other, exiter, Current, shares: 1));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(2);
        row.SoldOutFilerCount.Should().Be(1, "a filer with no current 13F proves no exit");
    }

    private async Task<MarketWideStockChurn> Churn(
        EquityIssuer stock,
        DateOnly current,
        DateOnly previous
    )
    {
        await using var read = FreshContext();
        var row = await new InstitutionalHoldingRepository(read)
            .GetQuarterlyNewSoldOutPositions(current, previous)
            .SingleOrDefaultAsync(r => r.CommonStockId == stock.Id);
        return row ?? new MarketWideStockChurn { CommonStockId = stock.Id };
    }

    private static async Task<EquityIssuer> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: $"{ticker} Test Corp.",
            Cik: $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}"
        );
        ctx.Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static async Task<InstitutionalHolder> SeedHolder(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string cik
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Holder {cik}" };
        ctx.Add(holder);
        await ctx.SaveChangesAsync();
        return holder;
    }

    private static InstitutionalHolding MakeHolding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
