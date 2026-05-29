using Equibles.CommonStocks.Data.Models;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// End-to-end pin for the backfill manager's core recompute loop, which needs a
/// real relational provider (it calls Database.SetCommandTimeout, unsupported by
/// the in-memory provider used elsewhere). Contract: Run cross-checks every
/// transaction's PricePerShare against the unadjusted close on its transaction
/// date and flips IsPriceValid per the validator (>10x close = invalid), tallying
/// MarkedInvalid / MarkedValid.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderTransactionPriceBackfillManagerRunTests : ParadeDbMcpTestBase
{
    public InsiderTransactionPriceBackfillManagerRunTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_RecomputesValidity_FlipsImplausiblePriceAndKeepsPlausible()
    {
        var date = new DateOnly(2024, 6, 14);
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001",
            Name = "Jane Insider",
            City = "Cupertino",
            StateOrCountry = "CA",
            IsDirector = true,
        };
        // Unadjusted close of $50 → validator ceiling is 10x = $500.
        var implausible = Transaction(stock, owner, date, pricePerShare: 50_000m, "ACC-BAD");
        var plausible = Transaction(stock, owner, date, pricePerShare: 55m, "ACC-OK");

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                Date = date,
                Close = 50m,
            }
        );
        DbContext.Add(implausible);
        DbContext.Add(plausible);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var runCtx = Fixture.CreateDbContext();
        var manager = new InsiderTransactionPriceBackfillManager(
            new InsiderTransactionRepository(runCtx),
            new DailyStockPriceRepository(runCtx),
            new InsiderTransactionPriceValidator(),
            runCtx,
            NullLogger<InsiderTransactionPriceBackfillManager>()
        );

        var result = await manager.Run();

        result.Total.Should().Be(2);
        result.Processed.Should().Be(2);
        result.MarkedInvalid.Should().Be(1);
        result.MarkedValid.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var bad = await verify.Set<InsiderTransaction>().FindAsync(implausible.Id);
        var ok = await verify.Set<InsiderTransaction>().FindAsync(plausible.Id);
        bad!.IsPriceValid.Should().BeFalse("$50,000 on a $50 close exceeds the 10x ceiling");
        ok!.IsPriceValid.Should().BeTrue("$55 on a $50 close is within the 10x ceiling");
    }

    private static InsiderTransaction Transaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly date,
        decimal pricePerShare,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            FilingDate = date,
            TransactionDate = date,
            TransactionCode = TransactionCode.Purchase,
            Shares = 1000,
            PricePerShare = pricePerShare,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = accession,
        };
}
