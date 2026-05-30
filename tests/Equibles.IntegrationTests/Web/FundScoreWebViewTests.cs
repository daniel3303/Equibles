using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the fund-score surfaces: the institutions index alpha column + alpha sort, and the
/// institution profile's performance-score card.
/// </summary>
[Collection(WebHostCollection.Name)]
public class FundScoreWebViewTests
{
    private readonly WebHostFixture _fixture;

    public FundScoreWebViewTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_SortByAlphaDesc_RanksHigherAlphaFirstAndRendersTheColumn()
    {
        var quarter = new DateOnly(2024, 12, 31);
        var aaplId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var laggardId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = winnerId,
                    Cik = "0000070",
                    Name = "Aardvark Alpha Fund",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = laggardId,
                    Cik = "0000080",
                    Name = "Aardvark Beta Fund",
                }
            );
            db.Add(MakeHolding(aaplId, winnerId, quarter));
            db.Add(MakeHolding(aaplId, laggardId, quarter));
            db.Add(MakeScore(winnerId, alpha: 12.3m));
            db.Add(MakeScore(laggardId, alpha: -4.5m));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?sort=AlphaDescending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("3Y alpha");
        // The view formats with InvariantCulture, so the separator is a dot regardless of host.
        // The leading "+" on positive values is HTML-encoded by Razor, so assert the number only.
        html.Should().Contain("12.3%");
        html.Should().Contain("-4.5%");
        var winnerIdx = html.IndexOf("Aardvark Alpha Fund", StringComparison.Ordinal);
        var laggardIdx = html.IndexOf("Aardvark Beta Fund", StringComparison.Ordinal);
        winnerIdx.Should().BeGreaterThan(-1);
        laggardIdx.Should().BeGreaterThan(winnerIdx, "the +12.3% fund outranks the -4.5% fund");
    }

    [Fact]
    public async Task GetInstitution_WithFundScore_RendersThePerformanceCard()
    {
        var quarter = new DateOnly(2024, 12, 31);
        var aaplId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        const string cik = "0000090";

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = aaplId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = cik,
                    Name = "Scored Capital",
                }
            );
            db.Add(MakeHolding(aaplId, holderId, quarter));
            db.Add(MakeScore(holderId, alpha: 7.5m));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/institutions/{cik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("institution-fund-score");
        html.Should().Contain("3-year performance vs SPY");
        html.Should().Contain("fund-score-alpha");
        // Positive alpha renders green; the leading "+" is HTML-encoded, so assert the number only.
        html.Should().Contain("7.50%");
        html.Should().Contain("text-success");
    }

    private static InstitutionalHolding MakeHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
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
