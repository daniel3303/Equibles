using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Helpers;
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
    public async Task ResolveByTickerIncludingDelisted_DelistedOwnerAnswersWhereCurrentResolutionMisses()
    {
        _dbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "GONE",
                Active: false,
                Name: "Delisted Co",
                Cik: "4001"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (current, currentError) = await _repository.ResolveByTicker("gone");
        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("gone");

        current.Should().BeNull();
        currentError.Should().Be("Stock 'gone' not found.");
        stock!.Name.Should().Be("Delisted Co");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTickerIncludingDelisted_CurrentOwnerWinsOverAFormerOne()
    {
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "REUSE",
                Active: false,
                Name: "Former owner",
                Cik: "4101"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "REUSE",
                Name: "Current owner",
                Cik: "4102"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("REUSE");

        stock!.Name.Should().Be("Current owner");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTickerIncludingDelisted_TwoDelistedClaimantsFailClosed()
    {
        _dbContext.AddRange(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "TWICE",
                Active: false,
                Name: "First former owner",
                Cik: "4201"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "TWICE",
                Active: false,
                Name: "Second former owner",
                Cik: "4202"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("TWICE");

        stock.Should().BeNull();
        error.Should().Be("Listed security 'TWICE' is ambiguous.");
    }

    [Fact]
    public async Task ResolveByTickerIncludingDelisted_DottedClassShareFallsBackToDelistedDashForm()
    {
        _dbContext.Add(
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "OLD-B",
                Active: false,
                Name: "Delisted class B",
                Cik: "4301"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("OLD.B");

        stock!.Name.Should().Be("Delisted class B");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTickerIncludingDelisted_ListedCompanysFormerTickerResolvesIt()
    {
        EquityIssuer renamed = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "NEWT",
            Name: "Renamed Co",
            Cik: "4401"
        );
        UsEquityDirectory.GetOrAddListing(renamed, "OLDT").Active = false;
        _dbContext.Add(renamed);
        await _dbContext.SaveChangesAsync();

        var (current, _) = await _repository.ResolveByTicker("OLDT");
        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("OLDT");

        current.Should().BeNull();
        stock!.Name.Should().Be("Renamed Co");
        error.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByTickerIncludingDelisted_TickerStillTradedOffTheDirectoryIsNotHandedBack()
    {
        EquityIssuer trader = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "MAIN",
            Name: "Still trades it",
            Cik: "4501"
        );
        UsEquityDirectory.GetOrAddListing(trader, "SIDE");
        _dbContext.AddRange(
            trader,
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Id: Guid.NewGuid(),
                Ticker: "SIDE",
                Active: false,
                Name: "Former owner",
                Cik: "4502"
            )
        );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTickerIncludingDelisted("SIDE");

        stock.Should().BeNull();
        error.Should().Be("Stock 'SIDE' not found.");
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
