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
/// against ParadeDB. A missing position counts as an entry or exit only for a filer that
/// reported a 13F for the other quarter.
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
    public async Task GetQuarterlyNewSoldOutPositions_HolderNewToTheStock_CountsAsNew()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "AAPL");
        EquityIssuer other = await SeedStock(seed, "KO");
        var holder = await SeedHolder(seed, "1");
        seed.Add(MakeHolding(other, holder, Prior, shares: 10));
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_000));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(1);
        row.SoldOutFilerCount.Should().Be(0);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_FirstTimeFiler_IsNoBuy()
    {
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "AAPL");
        var holder = await SeedHolder(seed, "11");
        seed.Add(MakeHolding(stock, holder, Current, shares: 1_000));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(0, "a first-time filer's book proves no entry");
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
    // full book the quarter before and after, but its 13F for the middle quarter is one placeholder
    // row, so that quarter holds no row and no rollup for it.
    [Fact]
    public async Task GetQuarterlyNewSoldOutPositions_QuarterWithoutAPublished13F_IsNeitherASaleNorABuy()
    {
        var before = new DateOnly(2025, 12, 31);
        var confidential = new DateOnly(2026, 3, 31);
        var after = new DateOnly(2026, 6, 30);
        await using var seed = FreshContext();
        EquityIssuer stock = await SeedStock(seed, "AAPL");
        var withheld = await SeedHolder(seed, "1374170");
        var seller = await SeedHolder(seed, "8");
        seed.Add(MakeHolding(stock, withheld, before, shares: 1_000));
        seed.Add(MakeHolding(stock, withheld, after, shares: 1_000));
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

        var exits = await Churn(stock, confidential, before);
        var entries = await Churn(stock, after, confidential);

        exits
            .SoldOutFilerCount.Should()
            .Be(
                1,
                "only the filer whose restatement emptied its book sold; the withheld book did not"
            );
        entries.NewFilerCount.Should().Be(0, "the withheld book was held all along, not bought");
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
        var firstFiler = await SeedHolder(seed, "9");
        var lapsed = await SeedHolder(seed, "10");
        // Prior: exiter + steady + lapsed (which files no current 13F); both newcomers filed other
        // positions.
        seed.Add(MakeHolding(stock, exiter, Prior, shares: 100));
        seed.Add(MakeHolding(stock, steady, Prior, shares: 200));
        seed.Add(MakeHolding(stock, lapsed, Prior, shares: 300));
        seed.Add(MakeHolding(other, newcomer1, Prior, shares: 1));
        seed.Add(MakeHolding(other, newcomer2, Prior, shares: 1));
        // Current: newcomer1 + newcomer2 + steady + a first-time filer; the exiter still files,
        // without this stock.
        seed.Add(MakeHolding(stock, newcomer1, Current, shares: 500));
        seed.Add(MakeHolding(stock, newcomer2, Current, shares: 600));
        seed.Add(MakeHolding(stock, steady, Current, shares: 200));
        seed.Add(MakeHolding(stock, firstFiler, Current, shares: 700));
        seed.Add(MakeHolding(other, exiter, Current, shares: 1));
        await seed.SaveChangesAsync();

        var row = await Churn(stock, Current, Prior);

        row.NewFilerCount.Should().Be(2, "a filer with no prior 13F proves no entry");
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
