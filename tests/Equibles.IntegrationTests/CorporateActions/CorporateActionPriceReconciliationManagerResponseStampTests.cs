using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(ParadeDbCollection.Name)]
public class CorporateActionPriceReconciliationManagerResponseStampTests : IAsyncLifetime
{
    private static readonly DateOnly SettledBefore = new(2026, 8, 10);
    private readonly ParadeDbFixture _fixture;

    public CorporateActionPriceReconciliationManagerResponseStampTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task StampApplied_RequestVariantDividendMatchingPriceResponse_StampsCurrentAmount()
    {
        var stockId = Guid.NewGuid();
        var exDate = new DateOnly(2026, 7, 31);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(new CommonStock { Id = stockId, Ticker = "GRTUF" });
            seed.Add(
                new CashDividend
                {
                    CommonStockId = stockId,
                    ExDate = exDate,
                    AmountPerShare = 0.21222556m,
                    Source = CashDividendSource.External,
                }
            );
            await seed.SaveChangesAsync();
        }

        PendingPriceReconciliationSeries selected;
        await using (var selection = _fixture.CreateDbContext())
        {
            selected = (
                await NewManager(selection).SelectPendingSeries(50, SettledBefore)
            ).Series.Single();
        }

        await using (var capture = _fixture.CreateDbContext())
        {
            var changes = await new CashDividendCaptureManager(
                new CashDividendRepository(capture),
                new CommonStockRepository(capture)
            ).Capture(
                stockId,
                "GRTUF",
                [
                    new CapturedDividend
                    {
                        ExDate = exDate,
                        AmountPerShare = 0.21219057m,
                        Source = CashDividendSource.Yahoo,
                    },
                ]
            );
            changes.Should().Be(1);
        }

        var appliedTime = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        await using (var stamping = _fixture.CreateDbContext())
        {
            var stamped = await NewManager(stamping)
                .StampApplied(
                    selected,
                    [
                        new CapturedDividend
                        {
                            ExDate = exDate,
                            AmountPerShare = 0.21219057m,
                            Source = CashDividendSource.Yahoo,
                        },
                    ],
                    SettledBefore,
                    appliedTime
                );

            stamped.Should().Be(1);
        }

        await using var verification = _fixture.CreateDbContext();
        var stored = await verification.Set<CashDividend>().SingleAsync();
        stored.Source.Should().Be(CashDividendSource.Yahoo);
        stored.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.21219057m);
        stored.PriceAdjustmentAppliedTime.Should().Be(appliedTime);

        var externalReplay = await new CashDividendCaptureManager(
            new CashDividendRepository(verification),
            new CommonStockRepository(verification)
        ).Capture(
            stockId,
            "GRTUF",
            [
                new CapturedDividend
                {
                    ExDate = exDate,
                    AmountPerShare = 0.21222556m,
                    Source = CashDividendSource.External,
                },
            ]
        );
        externalReplay.Should().Be(0);

        var afterReplay = await verification.Set<CashDividend>().SingleAsync();
        afterReplay.AmountPerShare.Should().Be(0.21219057m);
        afterReplay.Source.Should().Be(CashDividendSource.Yahoo);
        afterReplay.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.21219057m);
        afterReplay.PriceAdjustmentAppliedTime.Should().Be(appliedTime);
    }

    private static CorporateActionPriceReconciliationManager NewManager(
        Equibles.Data.EquiblesFinancialDbContext context
    ) =>
        new(
            new StockSplitRepository(context),
            new CashDividendRepository(context),
            new CommonStockRepository(context),
            new CorporateActionPriceReconciliationCursorRepository(context)
        );
}
