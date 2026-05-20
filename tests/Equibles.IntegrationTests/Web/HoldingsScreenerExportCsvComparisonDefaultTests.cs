using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class HoldingsScreenerExportCsvComparisonDefaultTests
{
    private readonly WebHostFixture _fixture;

    public HoldingsScreenerExportCsvComparisonDefaultTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact(
        Skip = "GH-1252 — ResolveScreenerDates defaults comparison to reportDates[1] regardless of selected; collapses to selected==comparison when selected is second-latest."
    )]
    public async Task ExportCsv_SelectedDateIsSecondLatest_DefaultComparisonIsNextOlderNotItself()
    {
        // Contract: the screener compares two quarters. When the user picks a
        // specific selected date and omits compareDate, the default comparison
        // should be the NEXT-OLDER quarter relative to selected — otherwise
        // selecting the second-latest collapses comparison to the same date
        // (every delta becomes 0), and selecting older than second-latest
        // points comparison at a *newer* date (deltas come back reversed).
        // The current fallback `comparison = reportDates[1]` ignores selected.
        var oldest = new DateOnly(2024, 3, 31);
        var middle = new DateOnly(2024, 6, 30);
        var newest = new DateOnly(2024, 9, 30);
        var holderId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = holderId,
                    Cik = "0008000301",
                    Name = "Default-Comparison Holder",
                }
            );
            var stock = new CommonStock
            {
                Ticker = "TRIQ",
                Name = "Three Quarters Co.",
                Cik = "0000099761",
            };
            db.Add(stock);
            db.Add(MakeHolding(stock.Id, holderId, oldest, 100, 100_000));
            db.Add(MakeHolding(stock.Id, holderId, middle, 110, 110_000));
            db.Add(MakeHolding(stock.Id, holderId, newest, 120, 120_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Holdings/Screener/Export.csv?date={middle:yyyy-MM-dd}"
        );

        response.EnsureSuccessStatusCode();
        var filename = response.Content.Headers.ContentDisposition!.FileName!.Trim('"');

        filename
            .Should()
            .Be(
                $"screener-{middle:yyyyMMdd}-vs-{oldest:yyyyMMdd}.csv",
                "the default comparison must be the next-older quarter relative to selected, not a fixed reportDates[1]"
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
}
