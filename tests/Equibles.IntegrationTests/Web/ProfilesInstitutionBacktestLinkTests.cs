using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins that the institution profile's action bar links to the new backtest route when
/// the holder has at least one 13F snapshot, and omits the link otherwise. Catches a
/// rename of the BacktestInstitution action or its route (asp-action resolves through
/// the real router under the in-process Web host).
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesInstitutionBacktestLinkTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesInstitutionBacktestLinkTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetInstitution_NoSnapshots_DoesNotRenderBacktestLink()
    {
        var cik = "0009100001";
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new InstitutionalHolder { Cik = cik, Name = "Empty Holder" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{cik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().NotContain("data-testid=\"institution-backtest-link\"");
    }

    [Fact]
    public async Task GetInstitution_WithSnapshot_RendersBacktestLinkPointingAtRoute()
    {
        var cik = "0009100002";
        var holderId = Guid.NewGuid();
        var stockId = Guid.NewGuid();

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
                    Cik = cik,
                    Name = "Snapshot Holder",
                }
            );
            db.Add(MakeHolding(stockId, holderId, new DateOnly(2024, 12, 31), 1_000, 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{cik}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"institution-backtest-link\"");
        // asp-action="BacktestInstitution" must resolve to the new route — if it doesn't,
        // the tag helper renders an empty href. The link is rendered without an explicit
        // controller, so the asp-route-cik resolves to the BacktestInstitution route which
        // attribute-routes to ~/Institutions/{cik}/Backtest.
        // URL generation lowercases by default — match case-insensitively so the test
        // doesn't break if a future LinkGenerator setting changes the casing again.
        html.ToLowerInvariant().Should().Contain($"/institutions/{cik}/backtest");
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
}
