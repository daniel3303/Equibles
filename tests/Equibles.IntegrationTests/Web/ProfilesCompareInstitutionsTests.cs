using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Compare Institutions page end-to-end: route resolves, missing CIKs are
/// reported, two-fund overlap renders the summary stats card and the side-by-side
/// stocks table, and the &gt;4 CIKs case returns 400.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesCompareInstitutionsTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesCompareInstitutionsTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetCompare_NoCiks_RendersPromptToProvideCiks()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/Institutions/Compare");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Pass at least two CIKs");
    }

    [Fact]
    public async Task GetCompare_TooManyCiks_Returns400()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync(
            "/Institutions/Compare?ciks=A&ciks=B&ciks=C&ciks=D&ciks=E"
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCompare_TwoFundsWithCommonQuarter_RendersOverlapTable()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var fundACik = "0002000001";
        var fundBCik = "0002000002";
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
                    Name = "Fund A LP",
                },
                new InstitutionalHolder
                {
                    Id = fundBId,
                    Cik = fundBCik,
                    Name = "Fund B LP",
                }
            );
            // Both funds hold AAPL; only Fund A holds MSFT.
            db.Add(MakeHolding(aaplId, fundAId, reportDate, value: 1_000_000));
            db.Add(MakeHolding(msftId, fundAId, reportDate, value: 500_000));
            db.Add(MakeHolding(aaplId, fundBId, reportDate, value: 800_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Compare?ciks={fundACik}&ciks={fundBCik}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"compare-overlap-summary\"");
        html.Should().Contain("data-testid=\"compare-overlap-table\"");
        html.Should().Contain("Jaccard similarity");
        html.Should().Contain("Fund A LP");
        html.Should().Contain("Fund B LP");
        html.Should().Contain("AAPL");
        html.Should().Contain("MSFT");
    }

    [Fact]
    public async Task GetCompare_UnknownCik_ReportsMissing()
    {
        var fundACik = "0002000003";
        var fundAId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = fundAId,
                    Cik = fundACik,
                    Name = "Fund A LP",
                }
            );
            await Task.CompletedTask;
        });

        // Fund A exists, Fund B does not.
        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Compare?ciks={fundACik}&ciks=does-not-exist"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Could not resolve CIKs");
        html.Should().Contain("does-not-exist");
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
            AccessionNumber = $"acc-{stockId:N}".Substring(0, 12) + $"-{reportDate:yyyyMMdd}",
        };
}
