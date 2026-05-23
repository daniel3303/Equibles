using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class ProfilesOverlapMatrixTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesOverlapMatrixTests(WebHostFixture fixture) => _fixture = fixture;

    // The overlap matrix page is a new feature (#1917) with zero integration
    // coverage. This test pins that the route resolves, the Razor view renders
    // without error, and the pairwise matrix table appears when two funds share
    // a common quarter — exercising routing → controller →
    // FundOverlapCalculator.ComputePairwiseOverlap → Razor view.
    [Fact]
    public async Task GetOverlapMatrix_TwoFundsWithCommonQuarter_RendersMatrixTable()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var fundACik = "0003000001";
        var fundBCik = "0003000002";
        var fundAId = Guid.NewGuid();
        var fundBId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);

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
            db.AddRange(
                new InstitutionalHolder
                {
                    Id = fundAId,
                    Cik = fundACik,
                    Name = "Matrix Fund A LP",
                },
                new InstitutionalHolder
                {
                    Id = fundBId,
                    Cik = fundBCik,
                    Name = "Matrix Fund B LP",
                }
            );
            db.Add(MakeHolding(aaplId, fundAId, reportDate, value: 1_000_000));
            db.Add(MakeHolding(msftId, fundAId, reportDate, value: 500_000));
            db.Add(MakeHolding(aaplId, fundBId, reportDate, value: 800_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Overlap?ciks={fundACik}&ciks={fundBCik}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"overlap-matrix-table\"");
        html.Should().Contain("Matrix Fund A LP");
        html.Should().Contain("Matrix Fund B LP");
    }

    private static InstitutionalHolding MakeHolding(
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
            Shares = value / 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stockId:N}"[..12] + $"-{reportDate:yyyyMMdd}",
        };
}
