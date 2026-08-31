using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.CommonStocks;

// Lane A (adversarial): ResolveByTicker normalizes the ticker before lookup, so
// a caller passing a ticker in the "wrong" case must still resolve the stored
// (uppercase) stock with a null error. If the Normalize step were dropped, the
// lowercase lookup would miss and return a not-found error instead.
public class CommonStockRepositoryExtensionsResolveByTickerTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CommonStockRepository _repository;

    public CommonStockRepositoryExtensionsResolveByTickerTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ResolveByTicker_TickerSuppliedInLowercase_ResolvesStoredUppercaseStock()
    {
        _dbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "AAPL",
                    Name = "Apple Inc",
                    Cik = "0000320193",
                }
            );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("  aapl  ");

        stock.Should().NotBeNull();
        stock.Ticker.Should().Be("AAPL");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_DottedClassShareFallsBackToStoredDashForm()
    {
        _dbContext.Add(new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BRK-B",
            Name = "Berkshire Hathaway Class B",
            Cik = "0001067983",
        });
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("BRK.B");

        stock!.Ticker.Should().Be("BRK-B");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_ExactDottedTickerWinsBeforeDashFallback()
    {
        _dbContext.AddRange(
            new CommonStock { Id = Guid.NewGuid(), Ticker = "TEST.B", Name = "Exact", Cik = "1001" },
            new CommonStock { Id = Guid.NewGuid(), Ticker = "TEST-B", Name = "Fallback", Cik = "1002" }
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("TEST.B");

        stock!.Name.Should().Be("Exact");
        error.Should().BeNull();
    }

    [Fact]
    public async Task GetByCikTolerant_UnpaddedInputResolvesPaddedPrimaryCik()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
        };
        _dbContext.Add(stock);
        await _dbContext.SaveChangesAsync();

        var resolved = await _repository.GetByCikTolerant("320193");

        resolved.Should().BeSameAs(stock);
    }

    [Fact]
    public async Task GetByCikTolerant_PaddedInputResolvesUnpaddedSecondaryCik()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "SURV",
            Name = "Surviving filer",
            Cik = "10",
            SecondaryCiks = ["320193"],
        };
        _dbContext.Add(stock);
        await _dbContext.SaveChangesAsync();

        var resolved = await _repository.GetByCikTolerant("0000320193");

        resolved.Should().BeSameAs(stock);
    }

    [Fact]
    public async Task GetByCikTolerant_CanonicalCollisionFailsClosed()
    {
        _dbContext.AddRange(
            new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = "PAD",
                Name = "Padded",
                Cik = "0000320193",
            },
            new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = "PLAIN",
                Name = "Plain",
                Cik = "320193",
            }
        );
        await _dbContext.SaveChangesAsync();

        (await _repository.GetByCikTolerant("0000320193")).Should().BeNull();
    }
}
