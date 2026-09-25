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
    private readonly EquityIssuerRepository _repository;

    public CommonStockRepositoryExtensionsResolveByTickerTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new EquityIssuerRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ResolveByTicker_TickerSuppliedInLowercase_ResolvesStoredUppercaseStock()
    {
        _dbContext
            .Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: "AAPL",
                    Name: "Apple Inc",
                    Cik: "0000320193"
                )
            );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("  aapl  ");

        stock.Should().NotBeNull();
        stock.Presentation.Listing.Ticker.Should().Be("AAPL");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_DottedClassShareFallsBackToStoredDashForm()
    {
        _dbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "BRK-B",
                Name: "Berkshire Hathaway Class B",
                Cik: "0001067983"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("BRK.B");

        stock!.Presentation.Listing.Ticker.Should().Be("BRK-B");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_ExactDottedTickerWinsBeforeDashFallback()
    {
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "TEST.B",
                Name: "Exact",
                Cik: "1001"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "TEST-B",
                Name: "Fallback",
                Cik: "1002"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("TEST.B");

        stock!.Name.Should().Be("Exact");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_ReferenceListedTickerResolvesItsOwner()
    {
        _dbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "GOOGL",
                Name: "Alphabet",
                Cik: "0001652044",
                ReferenceTickers: ["GOOG"]
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("GOOG");

        stock!.Name.Should().Be("Alphabet");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTicker_DefaultAndReferenceClaimsOnTwoIssuersFailClosed()
    {
        // The default owner claims DUP twice, so a duplicate-keeping read could fill both
        // owner slots with it and hide the second claimant.
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "DUP",
                Name: "Default owner",
                Cik: "2001",
                ReferenceTickers: ["DUP"]
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "OTHR",
                Name: "Reference owner",
                Cik: "2002",
                ReferenceTickers: ["DUP"]
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("DUP");

        stock.Should().BeNull();
        error.Should().Be("Listed security 'DUP' is ambiguous.");
    }

    [Fact]
    public async Task ResolveByTicker_DirectoryOnlySecondaryListingIsNotAnOwnerClaim()
    {
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "ONE",
                Name: "Default owner",
                Cik: "3001"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "TWO",
                Name: "Directory secondary",
                Cik: "3002",
                SecondaryTickers: ["ONE"]
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("ONE");

        stock!.Name.Should().Be("Default owner");
        error.Should().BeNull();
    }

    [Fact]
    public async Task GetByCikTolerant_UnpaddedInputResolvesPaddedPrimaryCik()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc",
            Cik: "0000320193"
        );
        _dbContext.Add(stock);
        await _dbContext.SaveChangesAsync();

        EquityIssuer resolved = await _repository.GetByCikTolerant("320193");

        resolved.Should().BeSameAs(stock);
    }

    [Fact]
    public async Task GetByCikTolerant_PaddedInputResolvesUnpaddedSecondaryCik()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SURV",
            Name: "Surviving filer",
            Cik: "10",
            SecondaryCiks: ["320193"]
        );
        _dbContext.Add(stock);
        await _dbContext.SaveChangesAsync();

        EquityIssuer resolved = await _repository.GetByCikTolerant("0000320193");

        resolved.Should().BeSameAs(stock);
    }

    [Fact]
    public async Task GetByCikTolerant_CanonicalCollisionFailsClosed()
    {
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "PAD",
                Name: "Padded",
                Cik: "0000320193"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "PLAIN",
                Name: "Plain",
                Cik: "320193"
            )
        );
        await _dbContext.SaveChangesAsync();

        (await _repository.GetByCikTolerant("0000320193")).Should().BeNull();
    }
}
