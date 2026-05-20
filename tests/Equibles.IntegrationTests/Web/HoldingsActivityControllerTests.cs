using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Holdings/Activity route end-to-end: route resolves, the controller
/// composes the per-stock activity rows from <c>GetQuarterlyActivity</c>, and
/// the rendered HTML carries the Top Buys / Top Sells panels with the right
/// stock identities. The seed forces one stock into each direction so a regression
/// in the controller's filter / orderby would surface as missing markup.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetActivity_NoHoldings_RendersEmptyState()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/Holdings/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("No 13F data yet");
        html.Should().NotContain("activity-top-buys");
        html.Should().NotContain("activity-top-sells");
    }

    [Fact]
    public async Task GetActivity_SingleQuarter_RendersNoPriorQuarterNotice()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var only = new DateOnly(2024, 12, 31);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "1",
                    Name = "Single Filer",
                }
            );
            db.Add(
                new InstitutionalHolding
                {
                    CommonStockId = stockId,
                    InstitutionalHolderId = holderId,
                    ReportDate = only,
                    FilingDate = only.AddDays(45),
                    Shares = 10_000,
                    Value = 1_000_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    AccessionNumber = "acc-1",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("No prior quarter available");
        // The Top Buys / Top Sells cards must NOT render without a prior quarter.
        html.Should().NotContain("data-testid=\"activity-top-buys\"");
        html.Should().NotContain("data-testid=\"activity-top-sells\"");
    }

    [Fact]
    public async Task GetActivity_TwoQuartersWithMovement_RendersTopBuysAndTopSells()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

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
                    Id = msftId,
                    Ticker = "MSFT",
                    Name = "Microsoft Corp.",
                    Cik = "0000789019",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "1",
                    Name = "Big Fund LP",
                }
            );
            // AAPL: increased (Δ +5_000 shares / +500_000 value) → Top Buys.
            db.Add(MakeHolding(aaplId, holderId, prior, shares: 10_000, value: 1_000_000));
            db.Add(MakeHolding(aaplId, holderId, current, shares: 15_000, value: 1_500_000));
            // MSFT: reduced (Δ -3_000 shares / -300_000 value) → Top Sells.
            db.Add(MakeHolding(msftId, holderId, prior, shares: 8_000, value: 800_000));
            db.Add(MakeHolding(msftId, holderId, current, shares: 5_000, value: 500_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // Both panels render.
        html.Should().Contain("data-testid=\"activity-top-buys\"");
        html.Should().Contain("data-testid=\"activity-top-sells\"");
        // The AAPL row lives in the buys panel; MSFT in the sells panel.
        var buysAnchor = html.IndexOf(
            "data-testid=\"activity-top-buys\"",
            StringComparison.Ordinal
        );
        var sellsAnchor = html.IndexOf(
            "data-testid=\"activity-top-sells\"",
            StringComparison.Ordinal
        );
        buysAnchor.Should().BeGreaterThan(-1);
        sellsAnchor.Should().BeGreaterThan(buysAnchor);
        var buysSection = html.Substring(buysAnchor, sellsAnchor - buysAnchor);
        var sellsSection = html.Substring(sellsAnchor);
        buysSection.Should().Contain("AAPL");
        buysSection.Should().NotContain("MSFT");
        sellsSection.Should().Contain("MSFT");
        sellsSection.Should().NotContain("AAPL");
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
            // AccessionNumber column caps at 32 chars; first 8 hex of the Guid + the date keep us well under.
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
