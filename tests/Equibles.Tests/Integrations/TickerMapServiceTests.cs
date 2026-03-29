using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Tests.Helpers;
using Equibles.Worker;

namespace Equibles.Tests.Integrations;

public class TickerMapServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly CommonStockRepository _stockRepo;
    private readonly TickerMapService _service;
    private readonly TickerMapService _clientEvalService;

    public TickerMapServiceTests() {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _stockRepo = new CommonStockRepository(_dbContext);

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), _stockRepo));
        _service = new TickerMapService(scopeFactory);

        // Separate service backed by a repository that forces client evaluation,
        // needed for GetByTickers because SecondaryTickers.Any() is untranslatable
        // by the EF Core in-memory provider.
        var clientEvalRepo = new ClientEvalStockRepository(_dbContext);
        var clientEvalScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), clientEvalRepo));
        _clientEvalService = new TickerMapService(clientEvalScopeFactory);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    private static CommonStock CreateStock(string ticker, string name, string cik = null) {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = cik ?? $"CIK-{ticker}",
        };
    }

    private async Task SeedStocks(params CommonStock[] stocks) {
        _stockRepo.AddRange(stocks);
        await _stockRepo.SaveChanges();
    }

    // ── Build with no stocks ───────────────────────────────────────────

    [Fact]
    public async Task Build_NoStocksExist_ReturnsEmptyDictionary() {
        var result = await _service.Build(null, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Build_EmptyTickerList_NoStocks_ReturnsEmptyDictionary() {
        var result = await _service.Build([], CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── Build returns all stocks when no filter ────────────────────────

    [Fact]
    public async Task Build_NullTickerList_ReturnsAllStocks() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        await SeedStocks(apple, msft);

        var result = await _service.Build(null, CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainKey("AAPL").WhoseValue.Should().Be(apple.Id);
        result.Should().ContainKey("MSFT").WhoseValue.Should().Be(msft.Id);
    }

    [Fact]
    public async Task Build_EmptyTickerList_ReturnsAllStocks() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var goog = CreateStock("GOOG", "Alphabet Inc");
        await SeedStocks(apple, goog);

        var result = await _service.Build([], CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainKey("AAPL").WhoseValue.Should().Be(apple.Id);
        result.Should().ContainKey("GOOG").WhoseValue.Should().Be(goog.Id);
    }

    // ── Build filters by tickers ──────────────────────────────────────
    // These tests use _clientEvalService because GetByTickers uses
    // SecondaryTickers.Any() which the in-memory provider cannot translate.

    [Fact]
    public async Task Build_WithTickerFilter_ReturnsOnlyMatchingStocks() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var goog = CreateStock("GOOG", "Alphabet Inc");
        await SeedStocks(apple, msft, goog);

        var result = await _clientEvalService.Build(["AAPL", "GOOG"], CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().ContainKey("AAPL").WhoseValue.Should().Be(apple.Id);
        result.Should().ContainKey("GOOG").WhoseValue.Should().Be(goog.Id);
        result.Should().NotContainKey("MSFT");
    }

    [Fact]
    public async Task Build_WithSingleTickerFilter_ReturnsSingleMapping() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        await SeedStocks(apple, msft);

        var result = await _clientEvalService.Build(["MSFT"], CancellationToken.None);

        result.Should().ContainSingle()
            .Which.Key.Should().Be("MSFT");
    }

    [Fact]
    public async Task Build_WithNonExistentTicker_ReturnsEmptyDictionary() {
        var apple = CreateStock("AAPL", "Apple Inc");
        await SeedStocks(apple);

        var result = await _clientEvalService.Build(["ZZZZ"], CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ── Build maps ticker to correct ID ───────────────────────────────

    [Fact]
    public async Task Build_MapsTickerToCorrectId() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        var goog = CreateStock("GOOG", "Alphabet Inc");
        await SeedStocks(apple, msft, goog);

        var result = await _service.Build(null, CancellationToken.None);

        result["AAPL"].Should().Be(apple.Id);
        result["MSFT"].Should().Be(msft.Id);
        result["GOOG"].Should().Be(goog.Id);
    }

    // ── Case-insensitive dictionary ───────────────────────────────────

    [Fact]
    public async Task Build_ReturnsCaseInsensitiveDictionary() {
        var apple = CreateStock("AAPL", "Apple Inc");
        await SeedStocks(apple);

        var result = await _service.Build(null, CancellationToken.None);

        result.Should().ContainKey("AAPL");
        result.Should().ContainKey("aapl");
        result.Should().ContainKey("Aapl");
        result["aapl"].Should().Be(apple.Id);
    }

    // ── Build with secondary tickers ──────────────────────────────────

    [Fact]
    public async Task Build_FilterMatchesSecondaryTicker_IncludesStock() {
        var brk = CreateStock("BRK.A", "Berkshire Hathaway");
        brk.SecondaryTickers = ["BRK.B"];
        await SeedStocks(brk);

        var result = await _clientEvalService.Build(["BRK.B"], CancellationToken.None);

        result.Should().ContainSingle()
            .Which.Value.Should().Be(brk.Id);
    }

    // ── Build handles many stocks ─────────────────────────────────────

    [Fact]
    public async Task Build_ManyStocks_ReturnsCompleteMapping() {
        var stocks = Enumerable.Range(1, 50)
            .Select(i => CreateStock($"T{i:D4}", $"Company {i}"))
            .ToArray();
        await SeedStocks(stocks);

        var result = await _service.Build(null, CancellationToken.None);

        result.Should().HaveCount(50);
        foreach (var stock in stocks) {
            result.Should().ContainKey(stock.Ticker)
                .WhoseValue.Should().Be(stock.Id);
        }
    }

    // ── CancellationToken is respected ────────────────────────────────

    [Fact]
    public async Task Build_CancelledToken_ThrowsOperationCancelled() {
        var apple = CreateStock("AAPL", "Apple Inc");
        await SeedStocks(apple);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await _service.Build(null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Separate Build calls reflect DB mutations ─────────────────────

    [Fact]
    public async Task Build_CalledAfterNewStockAdded_ReflectsNewData() {
        var apple = CreateStock("AAPL", "Apple Inc");
        await SeedStocks(apple);

        var firstResult = await _service.Build(null, CancellationToken.None);
        firstResult.Should().ContainSingle();

        var msft = CreateStock("MSFT", "Microsoft Corp");
        await SeedStocks(msft);

        var secondResult = await _service.Build(null, CancellationToken.None);
        secondResult.Should().HaveCount(2);
        secondResult.Should().ContainKey("MSFT").WhoseValue.Should().Be(msft.Id);
    }

    [Fact]
    public async Task Build_CalledAfterStockRemoved_ReflectsRemoval() {
        var apple = CreateStock("AAPL", "Apple Inc");
        var msft = CreateStock("MSFT", "Microsoft Corp");
        await SeedStocks(apple, msft);

        var firstResult = await _service.Build(null, CancellationToken.None);
        firstResult.Should().HaveCount(2);

        _stockRepo.Delete(apple);
        await _stockRepo.SaveChanges();

        var secondResult = await _service.Build(null, CancellationToken.None);
        secondResult.Should().ContainSingle()
            .Which.Key.Should().Be("MSFT");
    }

    // ── Filter with mix of existing and non-existing tickers ──────────

    [Fact]
    public async Task Build_FilterWithMixedExistingAndNonExisting_ReturnsOnlyExisting() {
        var apple = CreateStock("AAPL", "Apple Inc");
        await SeedStocks(apple);

        var result = await _clientEvalService.Build(["AAPL", "NOPE", "FAKE"], CancellationToken.None);

        result.Should().ContainSingle()
            .Which.Key.Should().Be("AAPL");
    }

    /// <summary>
    /// Repository subclass that forces client-side evaluation for GetAll,
    /// which makes GetByTickers work with the EF Core in-memory provider.
    /// The SecondaryTickers.Any() expression is untranslatable server-side,
    /// so GetAll returns a TestAsyncQueryable that supports both LINQ-to-Objects
    /// evaluation and IAsyncEnumerable for EF Core async methods.
    /// </summary>
    private sealed class ClientEvalStockRepository : CommonStockRepository {
        public ClientEvalStockRepository(EquiblesDbContext dbContext) : base(dbContext) { }

        public override IQueryable<CommonStock> GetAll() {
            return new TestAsyncQueryable<CommonStock>(base.GetAll().ToList());
        }
    }
}
