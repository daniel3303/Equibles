using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: when the holder exists and the benchmark stock exists but the holder
/// has zero 13F snapshots on file, `HoldingsBacktestService.Execute` must short-
/// circuit and surface a UI-friendly reason ("Holder has no 13F snapshots on
/// file.") instead of trying to backtest an empty portfolio. The companion
/// ProfilesInstitutionBacktestTests cover the BenchmarkNotFound and happy paths;
/// this pin covers the previously unverified empty-holdings branch.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsBacktestServiceNoSnapshotsTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsBacktestServiceNoSnapshotsTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetBacktest_HolderHasNo13FSnapshots_RendersNoSnapshotsReason()
    {
        var holderCik = "0002000099";
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "SPY",
                    Name = "SPDR S&P 500 ETF",
                    Cik = "0000884394",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = Guid.NewGuid(),
                    Cik = holderCik,
                    Name = "Empty Portfolio Capital",
                }
            );
            // No InstitutionalHolding rows seeded for this holder — that's the whole point.
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync($"/Institutions/{holderCik}/Backtest");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, html);
        html.Should().Contain("data-testid=\"backtest-reason\"");
        html.Should().Contain("Holder has no 13F snapshots on file.");
        html.Should().NotContain("data-testid=\"backtest-portfolio-summary\"");
    }
}
