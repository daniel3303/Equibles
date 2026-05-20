using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class HoldingsExportActivityOldestSelectedTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsExportActivityOldestSelectedTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact(
        Skip = "GH-1243 — HoldingsExportController.Activity fallback `previousDate = reportDates[1]` returns 200 with a degenerate same-date or backwards-time comparison when the oldest date is selected; expected 404."
    )]
    public async Task ExportActivity_SelectedDateIsOldestAvailable_ReturnsNotFoundBecauseNoPriorQuarter()
    {
        // Contract: the action compares the selected report date against the
        // immediately PRIOR quarter (commit description: "bundles all four boards
        // ... for the selected report date" — boards measure prior→current deltas).
        // The oldest snapshot has no temporally prior quarter, so the response
        // must be 404 — the same response the existing OnlyOneQuarter case already
        // returns when no comparable prior data exists. The current fallback,
        // `previousDate = reportDates[1]` (second-newest in a descending list),
        // either equals the selected date (when count == 2, comparing it to
        // itself) or jumps to a non-adjacent newer quarter, neither of which is
        // a legitimate "prior" period.
        var older = new DateOnly(2024, 9, 30);
        var newer = new DateOnly(2024, 12, 31);
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "OLDS",
                    Name = "Oldest Selected Co.",
                    Cik = "0008870501",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0009000601",
                    Name = "Solo Holder",
                }
            );
            db.Add(MakeHolding(stockId, holderId, older, 100, 1000));
            db.Add(MakeHolding(stockId, holderId, newer, 200, 2000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Export/Activity?date={older:yyyy-MM-dd}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
