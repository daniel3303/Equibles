using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the retry-ladder repair: pairs that HAVE a price but cannot be honestly valued (ambiguous
/// split basis, implausible close) must advance the ladder instead of freezing at their current
/// retry count — the escape that once left 828,603 production rows stuck at ValueRetryCount = 1 —
/// and an exhausted ladder must publish the filer's own value rather than a silent zero.
/// </summary>
public class HoldingsValueRecalculatorFiledFallbackTests : IDisposable
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
        new CorporateActionsModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly ILogger<HoldingsValueRecalculator> _logger = Substitute.For<
        ILogger<HoldingsValueRecalculator>
    >();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        var ctx = new EquiblesFinancialDbContext(options, Modules);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = CreateSharedContext();

                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);

                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return scopeFactory;
    }

    private HoldingsValueRecalculator CreateRecalculator() =>
        new(CreateScopeFactory(), _priceProvider, _logger);

    private void SetPrices(Dictionary<(Guid, string, DateOnly), decimal> prices)
    {
        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(prices);
    }

    private async Task<(CommonStock Stock, InstitutionalHolding Holding)> Seed(
        DateOnly reportDate,
        long shares,
        long? filedValue,
        int retryCount,
        DateTime? lastRetryAt
    )
    {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AZUL",
            Name = "Azul SA",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = Guid.NewGuid().ToString()[..10],
            Name = "Test Filer",
        };
        var holding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(30),
            Shares = shares,
            Value = 0L,
            FiledValue = filedValue,
            ValuePending = true,
            ValueRetryCount = retryCount,
            ValueLastRetryAt = lastRetryAt,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Leg A",
                    Shares = shares / 2,
                    Value = 0L,
                },
            ],
        };

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();
        return (stock, holding);
    }

    private async Task<InstitutionalHolding> Reload(Guid holdingId)
    {
        var verifyContext = CreateSharedContext();
        return await verifyContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .FirstAsync(h => h.Id == holdingId);
    }

    // ── Deferred pairs advance the ladder ──────────────────────────────

    [Fact]
    public async Task Recalculate_ImplausibleClose_StaysPendingAndAdvancesRetryCount()
    {
        var reportDate = new DateOnly(2024, 6, 30);
        var (stock, holding) = await Seed(
            reportDate,
            shares: 273_201,
            filedValue: 1_092_804L,
            retryCount: 0,
            lastRetryAt: DateTime.UtcNow.AddDays(-2)
        );

        // The corrupt-series close: a "price" no equity has ever traded at.
        SetPrices(new() { [(stock.Id, null, reportDate)] = 285_249_984m });

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue("an implausible close must not price anything");
        updated.Value.Should().Be(0L);
        updated.ValueRetryCount.Should().Be(1, "a refused price must advance the retry ladder");
    }

    [Fact]
    public async Task Recalculate_AmbiguousSplitBasis_AdvancesRetryCountInsteadOfFreezing()
    {
        var reportDate = new DateOnly(2024, 6, 30);
        var (stock, holding) = await Seed(
            reportDate,
            shares: 1000,
            filedValue: 150_000L,
            retryCount: 1,
            lastRetryAt: DateTime.UtcNow.AddDays(-8)
        );

        // A captured split after the report date whose price adjustment has not run: the stored
        // series straddles two bases, so the pair defers — and must still climb the ladder.
        var seedContext = CreateSharedContext();
        seedContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    EffectiveDate = reportDate.AddDays(10),
                    Numerator = 10,
                    Denominator = 1,
                    PriceAdjustmentAppliedTime = null,
                }
            );
        await seedContext.SaveChangesAsync();

        SetPrices(new() { [(stock.Id, null, reportDate)] = 150m });

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue();
        updated
            .ValueRetryCount.Should()
            .Be(2, "a basis-deferred pair must not freeze at its current retry count");
    }

    [Fact]
    public async Task Recalculate_PlausibleCloseBehindLargeRestatementFactor_DefersInsteadOfPublishing()
    {
        var reportDate = new DateOnly(2024, 6, 30);
        var (stock, holding) = await Seed(
            reportDate,
            shares: 1000,
            filedValue: null,
            retryCount: 0,
            lastRetryAt: DateTime.UtcNow.AddDays(-2)
        );

        // An applied 100:1 forward split after the report date restates the count ×100, so a
        // perfectly plausible $15,000 close implies $1.5M per as-filed share. The fallback
        // repair resets published rows above that bound, so publishing here would put the two
        // passes in a permanent reset/re-derive loop — the pair must defer instead.
        var seedContext = CreateSharedContext();
        seedContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    EffectiveDate = reportDate.AddDays(10),
                    Numerator = 100,
                    Denominator = 1,
                    PriceAdjustmentAppliedTime = DateTime.UtcNow.AddDays(-1),
                }
            );
        await seedContext.SaveChangesAsync();

        SetPrices(new() { [(stock.Id, null, reportDate)] = 15_000m });

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue("the effective per-share price is implausible");
        updated.Value.Should().Be(0L);
        updated.ValueRetryCount.Should().Be(1, "a refused derivation must advance the ladder");
    }

    // ── Ladder exhaustion publishes the filed value ────────────────────

    [Fact]
    public async Task Recalculate_ExhaustedLadderWithFiledValue_PublishesFiledValue()
    {
        var reportDate = new DateOnly(2024, 3, 31);
        var (_, holding) = await Seed(
            reportDate,
            shares: 1000,
            filedValue: 868_524L,
            retryCount: 3,
            lastRetryAt: DateTime.UtcNow.AddDays(-31)
        );

        // No price at all for the pair — the ladder's final rung expires.
        SetPrices([]);

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.ValueUnavailable.Should().BeFalse();
        updated.Value.Should().Be(868_524L, "the filer's own figure beats a silent zero");
        updated.ValueSource.Should().Be(ValueSource.Filed);
        updated
            .ManagerEntries.Single()
            .Value.Should()
            .Be(434_262L, "legs split the filed value in proportion to their shares");
    }

    [Fact]
    public async Task Recalculate_ExhaustedLadderWithoutFiledValue_MarksValueUnavailable()
    {
        var reportDate = new DateOnly(2024, 3, 31);
        var (_, holding) = await Seed(
            reportDate,
            shares: 1000,
            filedValue: null,
            retryCount: 3,
            lastRetryAt: DateTime.UtcNow.AddDays(-31)
        );

        SetPrices([]);

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.Value.Should().Be(0L);
        updated
            .ValueUnavailable.Should()
            .BeTrue("a zero with no filed figure must read as unknown, not as nothing");
    }

    // ── Gross disagreement with the filed value on resolve ─────────────

    [Fact]
    public async Task Recalculate_DerivedValueGrosslyExceedsFiled_PublishesFiledValueInstead()
    {
        var reportDate = new DateOnly(2024, 6, 30);
        var (stock, holding) = await Seed(
            reportDate,
            shares: 273_201,
            filedValue: 1_092_804L,
            retryCount: 0,
            lastRetryAt: null
        );

        // Plausible as a share price, wrong for this stock by four orders of magnitude —
        // derived $13.66B against a filed $1.09M.
        SetPrices(new() { [(stock.Id, null, reportDate)] = 50_000m });

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.Value.Should().Be(1_092_804L);
        updated.ValueSource.Should().Be(ValueSource.Filed);
    }

    [Fact]
    public async Task Recalculate_FiledValueOnThousandsBasis_PublishesDerivedValue()
    {
        var reportDate = new DateOnly(2026, 3, 31);
        // The Baupost signature: a filer still reporting the VALUE column in thousands after
        // the SEC's 2023 whole-dollar switch. 3,118,754 shares × $208.40 derives ~$650M against
        // a filed 649,543 — almost exactly 1,000×. The old guard read that as an implausible
        // derivation and published the filed figure, serving the position 1,000× understated.
        var (stock, holding) = await Seed(
            reportDate,
            shares: 3_118_754,
            filedValue: 649_543L,
            retryCount: 0,
            lastRetryAt: null
        );

        SetPrices(new() { [(stock.Id, null, reportDate)] = 208.40m });

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.Value.Should().Be((long)(3_118_754m * 208.40m));
        updated.ValueSource.Should().Be(ValueSource.Derived);
    }
}
