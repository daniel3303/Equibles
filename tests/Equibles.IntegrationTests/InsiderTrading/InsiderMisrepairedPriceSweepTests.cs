using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Media.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.InsiderTrading;

/// <summary>
/// Pins the misrepaired-price restore: rows the old basis-blind validator "repaired" carry the
/// signature <c>|PricePerShare × Shares − ReportedPricePerShare| &lt; 0.01</c> with diverging
/// prices; the sweep restores the as-filed price and nulls the verdict so the fixed validator
/// re-evaluates. Rows the band-guarded repair stamped (<c>PriceWasRepaired</c>) are never
/// touched, and a second pass is a no-op.
/// </summary>
public class InsiderMisrepairedPriceSweepTests : IDisposable
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new CorporateActionsModuleConfiguration(),
        new MediaModuleConfiguration(),
        new InsiderTradingModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
    }

    private EquiblesFinancialDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        var ctx = new EquiblesFinancialDbContext(options, Modules);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    private InsiderMisrepairedPriceSweep CreateSweep()
    {
        var ctx = CreateContext();
        return new InsiderMisrepairedPriceSweep(
            new InsiderTransactionRepository(ctx),
            ctx,
            Substitute.For<ILogger<InsiderMisrepairedPriceSweep>>()
        );
    }

    private async Task<InsiderTransaction> Seed(
        decimal pricePerShare,
        decimal reportedPricePerShare,
        long shares,
        bool? isPriceValid = true,
        bool priceWasRepaired = false
    )
    {
        var ctx = CreateContext();
        var row = new InsiderTransaction
        {
            Id = Guid.NewGuid(),
            CommonStockId = Guid.NewGuid(),
            InsiderOwnerId = Guid.NewGuid(),
            AccessionNumber = Guid.NewGuid().ToString()[..20],
            TransactionOrder = 0,
            FilingDate = new DateOnly(2021, 2, 18),
            TransactionDate = new DateOnly(2021, 2, 16),
            TransactionCode = TransactionCode.Sale,
            Shares = shares,
            PricePerShare = pricePerShare,
            ReportedPricePerShare = reportedPricePerShare,
            IsPriceValid = isPriceValid,
            PriceWasRepaired = priceWasRepaired,
            AcquiredDisposed = AcquiredDisposed.Disposed,
            OwnershipNature = OwnershipNature.Direct,
            SecurityTitle = "Common Stock",
            SecurityKind = InsiderSecurityKind.NonDerivative,
        };
        ctx.Add(row);
        await ctx.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task Run_MisrepairedRow_RestoresAsFiledPriceAndNullsTheVerdict()
    {
        // The production signature: Jassy's $3,300.24 divided by 392 shares → $8.42, sealed
        // valid. 8.42 × 392 = 3,300.64… stored with full precision: 3300.24/392 × 392 lands
        // back within a cent of the as-filed figure.
        var row = await Seed(
            pricePerShare: 3_300.24m / 392m,
            reportedPricePerShare: 3_300.24m,
            shares: 392
        );

        var restored = await CreateSweep().Run(CancellationToken.None);

        restored.Should().Be(1);
        var verify = CreateContext();
        var updated = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id);
        updated.PricePerShare.Should().Be(3_300.24m);
        updated
            .IsPriceValid.Should()
            .BeNull("the fixed validator re-evaluates through the pending path");
    }

    [Fact]
    public async Task Run_BandGuardedRepair_IsNeverRestored()
    {
        // A repair the NEW validator made carries the stamp — restoring it would undo a
        // correct fix every cycle forever.
        var row = await Seed(
            pricePerShare: 50m,
            reportedPricePerShare: 1_000_000m,
            shares: 20_000,
            priceWasRepaired: true
        );

        var restored = await CreateSweep().Run(CancellationToken.None);

        restored.Should().Be(0);
        var verify = CreateContext();
        var updated = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id);
        updated.PricePerShare.Should().Be(50m);
        updated.IsPriceValid.Should().BeTrue();
    }

    [Fact]
    public async Task Run_OrdinaryValidRow_IsLeftAlone()
    {
        var row = await Seed(pricePerShare: 165.01m, reportedPricePerShare: 165.01m, shares: 392);

        var restored = await CreateSweep().Run(CancellationToken.None);

        restored.Should().Be(0);
        (await CreateContext().Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id))
            .IsPriceValid.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Run_SecondPass_IsANoOp()
    {
        await Seed(pricePerShare: 3_300.24m / 392m, reportedPricePerShare: 3_300.24m, shares: 392);

        (await CreateSweep().Run(CancellationToken.None)).Should().Be(1);
        (await CreateSweep().Run(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task ReopenShareslessVerdicts_StrandedShareslessRow_NullsTheVerdict()
    {
        // The old basis-blind comparison flagged the row invalid via its shares <= 0 branch;
        // the price was never touched, so only the verdict re-opens for the fixed validator.
        var row = await Seed(
            pricePerShare: 3_300.24m,
            reportedPricePerShare: 3_300.24m,
            shares: 0,
            isPriceValid: false
        );

        var reopened = await CreateSweep().ReopenShareslessVerdicts(CancellationToken.None);

        reopened.Should().Be(1);
        var verify = CreateContext();
        var updated = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id);
        updated.PricePerShare.Should().Be(3_300.24m, "the price was never corrupted");
        updated.IsPriceValid.Should().BeNull();
    }

    [Fact]
    public async Task ReopenShareslessVerdicts_RowWithShares_IsNotTouched()
    {
        // An invalid verdict with a positive share count came from the repair-refusal path,
        // not the sharesless branch — it stays.
        var row = await Seed(
            pricePerShare: 1_000_000m,
            reportedPricePerShare: 1_000_000m,
            shares: 1_000,
            isPriceValid: false
        );

        (await CreateSweep().ReopenShareslessVerdicts(CancellationToken.None)).Should().Be(0);
        (await CreateContext().Set<InsiderTransaction>().SingleAsync(t => t.Id == row.Id))
            .IsPriceValid.Should()
            .BeFalse();
    }

    [Fact]
    public async Task Run_DoesNotTouchTheShareslessCohort()
    {
        // The two passes are separate on purpose: Run is self-terminating and safe every
        // cycle; the sharesless reopen is one-shot (worker-gated) because a re-flagged row
        // matches its predicate again.
        await Seed(
            pricePerShare: 3_300.24m,
            reportedPricePerShare: 3_300.24m,
            shares: 0,
            isPriceValid: false
        );

        (await CreateSweep().Run(CancellationToken.None)).Should().Be(0);
    }
}
