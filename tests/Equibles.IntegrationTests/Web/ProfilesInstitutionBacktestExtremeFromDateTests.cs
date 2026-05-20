using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionBacktestExtremeFromDateTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionBacktestExtremeFromDateTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetBacktest_FromAtDateOnlyMinValue_DoesNotReturn500()
    {
        // Contract (per the slice commit): "benchmark-not-found and no-rebalance-in-window
        // cases render inline alerts rather than 500s." A from-date the user can plausibly
        // pass via the URL (`?from=0001-01-01`) must not crash the controller — but the
        // service computes `priceWindowFrom = resolvedFrom.AddDays(-14)`, which throws
        // ArgumentOutOfRangeException because DateOnly cannot represent dates before
        // year 1. The exception escapes the controller as a 500.
        var holderCik = "0002000099";
        var holderId = Guid.NewGuid();
        var aaplId = Guid.NewGuid();
        var spyId = Guid.NewGuid();
        var q1 = new DateOnly(2024, 3, 31);
        var rebalanceQ1 = q1.AddDays(45);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.AddRange(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                },
                new CommonStock
                {
                    Id = spyId,
                    Ticker = "SPY",
                    Name = "SPDR S&P 500 ETF",
                    Cik = "0000884394",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = holderCik,
                    Name = "Extreme-Date Capital",
                }
            );
            db.Add(MakeHolding(aaplId, holderId, q1, shares: 10_000, value: 1_000_000));
            for (var d = rebalanceQ1.AddDays(-7); d <= rebalanceQ1.AddDays(30); d = d.AddDays(1))
            {
                db.Add(MakePrice(aaplId, d, 100m));
                db.Add(MakePrice(spyId, d, 400m));
            }
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/{holderCik}/Backtest?from=0001-01-01"
        );

        response
            .StatusCode.Should()
            .NotBe(
                HttpStatusCode.InternalServerError,
                "the controller must render an inline alert for unreachable windows, not 500"
            );
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };

    private static DailyStockPrice MakePrice(Guid stockId, DateOnly date, decimal close) =>
        new()
        {
            CommonStockId = stockId,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000_000,
        };
}
