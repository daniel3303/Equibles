using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to InstitutionsControllerTests.GetIndex_SortByPositionsDesc (which
/// pins the PositionsDescending arm). The InstitutionSort switch has THREE
/// arms — default (Name asc), PositionsDescending, ValueDescending — and the
/// ValueDescending arm is unpinned. A refactor that mistakenly reused the
/// PositionsDescending key in the ValueDescending case (a tempting copy-paste
/// cleanup) would silently flip the table to a positions-count sort when the
/// UI requests a book-size sort, with no static or compile-time check
/// catching it. Pin the value-ordered output explicitly.
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerSortByValueDescendingTests
{
    private readonly WebHostFixture _fixture;

    public InstitutionsControllerSortByValueDescendingTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task GetIndex_SortByValueDesc_PutsHigherTotalValueFirst()
    {
        var quarter = new DateOnly(2024, 12, 31);
        var aaplId = Guid.NewGuid();
        var bigBookId = Guid.NewGuid();
        var smallBookId = Guid.NewGuid();

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
                    Id = bigBookId,
                    Cik = "0000050",
                    Name = "Aardvark Whale Fund",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = smallBookId,
                    Cik = "0000060",
                    Name = "Aardvark Minnow Fund",
                }
            );

            // Names start with the same prefix so a name-asc fallback would
            // tie-break alphabetically (Minnow < Whale) — but the value-desc
            // contract must put the Whale first.
            db.Add(MakeHoldingWithValue(aaplId, bigBookId, quarter, value: 100_000_000));
            db.Add(MakeHoldingWithValue(aaplId, smallBookId, quarter, value: 1_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?sort=ValueDescending");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        var whaleIdx = html.IndexOf("Aardvark Whale Fund", StringComparison.Ordinal);
        var minnowIdx = html.IndexOf("Aardvark Minnow Fund", StringComparison.Ordinal);
        whaleIdx.Should().BeGreaterThan(-1);
        minnowIdx.Should().BeGreaterThan(whaleIdx, "Whale's $100M book outranks Minnow's $1k");
    }

    private static InstitutionalHolding MakeHoldingWithValue(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long value
    ) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = 1_000,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{holderId:N}".Substring(0, 12) + $"-{stockId:N}".Substring(0, 8),
        };
}
