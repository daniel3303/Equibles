using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data.Models;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the smart-money index page: the consensus basket of the top-scoring funds' common
/// holdings renders, below-threshold stocks are excluded, and the performance chart is drawn.
/// </summary>
[Collection(WebHostCollection.Name)]
public class SmartMoneyIndexViewTests
{
    private static readonly DateOnly Quarter = new(2024, 12, 31);

    private readonly WebHostFixture _fixture;

    public SmartMoneyIndexViewTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetSmartMoneyIndex_WithConsensusHoldings_RendersBasketAndTracksPerformance()
    {
        var spyId = Guid.NewGuid();
        var consensusAId = Guid.NewGuid();
        var consensusBId = Guid.NewGuid();
        var soloId = Guid.NewGuid();
        var funds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(spyId, "SPY", "S&P 500 ETF"));
            db.Add(MakeStock(consensusAId, "WIDE", "Wide Consensus Co"));
            db.Add(MakeStock(consensusBId, "BOTH", "Both Funds Co"));
            db.Add(MakeStock(soloId, "SOLO", "Lonely Holding Co"));

            // Benchmark flat; the two consensus names appreciate so the index posts a return.
            AddPrice(db, spyId, 100m, 100m);
            AddPrice(db, consensusAId, 100m, 150m);
            AddPrice(db, consensusBId, 100m, 120m);
            AddPrice(db, soloId, 100m, 100m);

            decimal alpha = 30m;
            foreach (var fundId in funds)
            {
                db.Add(
                    new InstitutionalHolder
                    {
                        Id = fundId,
                        Cik = $"000{fundId.ToString("N")[..4]}",
                        Name = $"Top Fund {fundId:N}".Substring(0, 16),
                    }
                );
                // Every top fund holds the two consensus names.
                db.Add(MakeHolding(consensusAId, fundId));
                db.Add(MakeHolding(consensusBId, fundId));
                db.Add(MakeScore(fundId, alpha));
                alpha -= 10m;
            }

            // SOLO is held by only one fund — below the default consensus threshold.
            db.Add(MakeHolding(soloId, funds[0]));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions/smart-money-index");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        html.Should().Contain("Smart Money Index");
        html.Should().Contain("smart-money-constituents");
        html.Should().Contain("WIDE");
        html.Should().Contain("BOTH");
        // SOLO is held by a single fund and must not make the basket.
        html.Should().NotContain("SOLO");
        // The basket appreciated against a flat benchmark — the index summary card renders.
        html.Should().Contain("smart-money-index-summary");
        html.Should().Contain("smart-money-chart");
    }

    [Fact]
    public async Task GetSmartMoneyIndex_NoScoredFunds_RendersReasonWithoutError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(Guid.NewGuid(), "SPY", "S&P 500 ETF"));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions/smart-money-index");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("smart-money-reason");
        html.Should().Contain("No fund scores");
    }

    private static CommonStock MakeStock(Guid id, string ticker, string name) =>
        new()
        {
            Id = id,
            Ticker = ticker,
            Name = name,
            Cik = $"C{id.ToString("N")[..9]}",
        };

    private static void AddPrice(
        Equibles.Data.EquiblesFinancialDbContext db,
        Guid stockId,
        decimal early,
        decimal late
    )
    {
        // The early close must land inside the backtest's price window (rebalance − 14 days) and
        // on or before the rebalance date so day-zero forward-fills to it. The basket reflects the
        // 2024-12-31 quarter, so rebalance is 2025-02-14.
        db.Add(MakePrice(stockId, new DateOnly(2025, 2, 10), early));
        db.Add(MakePrice(stockId, new DateOnly(2026, 5, 1), late));
    }

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
        };

    private static InstitutionalHolding MakeHolding(Guid stockId, Guid holderId) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = Quarter,
            FilingDate = Quarter.AddDays(45),
            Shares = 1_000,
            Value = 1_000_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{holderId:N}".Substring(0, 12) + $"-{stockId:N}".Substring(0, 8),
        };

    private static FundScore MakeScore(Guid holderId, decimal alpha) =>
        new()
        {
            InstitutionalHolderId = holderId,
            BenchmarkTicker = "SPY",
            WindowYears = 3,
            WindowStart = new DateOnly(2021, 12, 31),
            WindowEnd = new DateOnly(2024, 12, 31),
            PortfolioCagrPercent = alpha + 8m,
            BenchmarkCagrPercent = 8m,
            PortfolioTotalReturnPercent = (alpha + 8m) * 3m,
            BenchmarkTotalReturnPercent = 24m,
            AlphaPercent = alpha,
            MaxDrawdownPercent = 15m,
        };
}
