using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.InsiderTrading;

public class InsiderTransactionRepositoryGetRecentByTypeTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InsiderTransactionRepository _repository;

    public InsiderTransactionRepositoryGetRecentByTypeTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new InsiderTradingModuleConfiguration()
        );
        _repository = new InsiderTransactionRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // GetRecentByType's contract is a conjunction: TransactionCode == code AND
    // TransactionDate >= since. This single test seeds one row per way the filter
    // can be wrong — the on-boundary match that must survive, an earlier-date row
    // of the same code that the date predicate must drop, and an on/after-date row
    // of a different code that the code predicate must drop — so the test fails if
    // either predicate is missing or the boundary is treated as exclusive.
    [Fact]
    public async Task GetRecentByType_FiltersByCodeAndIncludesSinceBoundary()
    {
        var since = new DateOnly(2024, 6, 10);
        var stock = CreateStock();
        var owner = CreateOwner();
        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InsiderOwner>().Add(owner);

        var onBoundary = CreateTransaction(
            stock,
            owner,
            code: TransactionCode.Purchase,
            transactionDate: since,
            accessionNumber: "0001234567-24-000001"
        );
        var sameCodeBeforeSince = CreateTransaction(
            stock,
            owner,
            code: TransactionCode.Purchase,
            transactionDate: since.AddDays(-1),
            accessionNumber: "0001234567-24-000002"
        );
        var otherCodeAfterSince = CreateTransaction(
            stock,
            owner,
            code: TransactionCode.Sale,
            transactionDate: since.AddDays(5),
            accessionNumber: "0001234567-24-000003"
        );
        _dbContext
            .Set<InsiderTransaction>()
            .AddRange(onBoundary, sameCodeBeforeSince, otherCodeAfterSince);
        await _dbContext.SaveChangesAsync();

        var result = await _repository
            .GetRecentByType(TransactionCode.Purchase, since)
            .ToListAsync();

        result.Should().ContainSingle().Which.Id.Should().Be(onBoundary.Id);
    }

    private static CommonStock CreateStock(string ticker = "AAPL", string name = "Apple Inc.") =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
        };

    private static InsiderOwner CreateOwner(string cik = "0001234567", string name = "John Doe") =>
        new()
        {
            Id = Guid.NewGuid(),
            OwnerCik = cik,
            Name = name,
            City = "New York",
            StateOrCountry = "NY",
            IsDirector = true,
        };

    private static InsiderTransaction CreateTransaction(
        CommonStock stock,
        InsiderOwner owner,
        TransactionCode code,
        DateOnly transactionDate,
        string accessionNumber
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            CommonStock = stock,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            FilingDate = transactionDate.AddDays(1),
            TransactionDate = transactionDate,
            TransactionCode = code,
            Shares = 1000,
            PricePerShare = 150.00m,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = accessionNumber,
            IsAmendment = false,
        };
}
