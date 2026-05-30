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
/// the in-memory provider used elsewhere). Contract: Run evaluates only the
/// not-yet-checked rows (IsPriceValid == null), cross-checks each reported price
/// against the unadjusted close on its transaction date, repairs implausible
/// rows (reported total ÷ shares) while preserving ReportedPricePerShare, and
/// tallies Repaired / Valid / Invalid / Pending.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderTransactionPriceBackfillManagerRunTests : ParadeDbMcpTestBase
{
    public InsiderTransactionPriceBackfillManagerRunTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_RepairsImplausiblePrice_AndKeepsPlausible()
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
        result.Repaired.Should().Be(1);
        result.Valid.Should().Be(1);
        result.Invalid.Should().Be(0);
        result.Pending.Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var bad = await verify.Set<InsiderTransaction>().FindAsync(implausible.Id);
        var ok = await verify.Set<InsiderTransaction>().FindAsync(plausible.Id);

        // Repaired: $50,000 on a $50 close exceeds the 10x ceiling, so the
        // mis-entered total is divided by the 1,000 shares back to $50/share.
        bad!.IsPriceValid.Should().BeTrue();
        bad.PricePerShare.Should().Be(50m);
        bad.ReportedPricePerShare.Should().Be(50_000m, "the as-filed value is preserved");

        // Plausible: $55 on a $50 close is within the ceiling — untouched.
        ok!.IsPriceValid.Should().BeTrue();
        ok.PricePerShare.Should().Be(55m);
        ok.ReportedPricePerShare.Should().Be(55m);
    }

    [Fact]
    public async Task Run_NoClose_StaysPending_AndZeroShares_IsInvalidNotRepaired()
    {
        var date = new DateOnly(2024, 6, 14);
        var priced = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        // Second stock has no DailyStockPrice on file → no usable close.
        var unpriced = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NONE",
            Name = "Unlisted Co.",
            Cik = "0000000001",
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
        // Implausible ($50,000 on a $50 close) but 0 shares → can't divide.
        var unrepairable = Transaction(priced, owner, date, 50_000m, "ACC-ZERO", shares: 0);
        // Real price, no close on file → must stay pending (null), not valid.
        var pending = Transaction(unpriced, owner, date, 12.34m, "ACC-PENDING");

        DbContext.Add(priced);
        DbContext.Add(unpriced);
        DbContext.Add(owner);
        DbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = priced.Id,
                Date = date,
                Close = 50m,
            }
        );
        DbContext.Add(unrepairable);
        DbContext.Add(pending);
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
        result.Repaired.Should().Be(0);
        result.Valid.Should().Be(0);
        result.Invalid.Should().Be(1);
        result.Pending.Should().Be(1);

        await using var verify = Fixture.CreateDbContext();
        var zero = await verify.Set<InsiderTransaction>().FindAsync(unrepairable.Id);
        var pend = await verify.Set<InsiderTransaction>().FindAsync(pending.Id);

        // Unrepairable: positively rejected, price left as filed.
        zero!.IsPriceValid.Should().BeFalse();
        zero.PricePerShare.Should().Be(50_000m);

        // Pending: no close yet, so it stays null for a later run to retry.
        pend!.IsPriceValid.Should().BeNull();
        pend.PricePerShare.Should().Be(12.34m);
    }

    private static InsiderTransaction Transaction(
        CommonStock stock,
        InsiderOwner owner,
        DateOnly date,
        decimal pricePerShare,
        string accession,
        long shares = 1000
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InsiderOwnerId = owner.Id,
            FilingDate = date,
            TransactionDate = date,
            TransactionCode = TransactionCode.Purchase,
            Shares = shares,
            PricePerShare = pricePerShare,
            // As-filed value, always populated by the migration / ingest;
            // IsPriceValid left null so Run treats the row as unevaluated.
            ReportedPricePerShare = pricePerShare,
            AcquiredDisposed = AcquiredDisposed.Acquired,
            SharesOwnedAfter = 5000,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            AccessionNumber = accession,
        };
}
