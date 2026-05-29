using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the DoubleDown action's data-present render path (previously only the
/// no-data and clamp paths were covered): a holder whose position grew past the
/// 50% default threshold quarter-over-quarter must surface in the positions
/// table. Exercises the controller's query + ordering + paging + render wiring.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsActivityControllerDoubleDownWithDataTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsActivityControllerDoubleDownWithDataTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task DoubleDown_PositionAboveDefaultThreshold_RendersInPositionsTable()
    {
        var aaplId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

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
                    Cik = "0001999999",
                    Name = "Conviction Capital",
                }
            );
            // 100 -> 300 shares = +200%, well past the 50% default threshold.
            db.Add(MakeHolding(aaplId, holderId, prior, shares: 100, value: 1_000_000));
            db.Add(MakeHolding(aaplId, holderId, current, shares: 300, value: 3_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/holdings/double-down-report");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"double-down-table\""); // positions table rendered
        html.Should().Contain("AAPL"); // the qualifying position appears
        html.Should().NotContain("No positions match"); // not the empty state
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
            AccessionNumber =
                $"acc-{stockId:N}".Substring(0, 12)
                + $"-{holderId:N}".Substring(0, 8)
                + $"-{reportDate:yyyyMMdd}",
        };
}
