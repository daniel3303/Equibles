using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the Combined Institutions page end-to-end: route resolves, missing CIKs are
/// reported, three-fund consensus ordering surfaces an all-funds-held stock above a
/// half-held stock, and the &gt;25 CIKs case returns 400.
/// </summary>
[Collection(WebHostCollection.Name)]
public class ProfilesCombinedInstitutionsTests
{
    private readonly WebHostFixture _fixture;

    public ProfilesCombinedInstitutionsTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetCombined_NoCiks_RendersPromptToProvideCiks()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        var response = await _fixture.Client.GetAsync("/Institutions/Combined");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Pass at least two CIKs");
    }

    [Fact]
    public async Task GetCombined_TooManyCiks_Returns400()
    {
        await _fixture.ResetAndSeedAsync(_ => Task.CompletedTask);

        // 26 CIKs (exceeds the 25 cap).
        var ciks = string.Join("&", Enumerable.Range(1, 26).Select(i => $"ciks=cik{i}"));
        var response = await _fixture.Client.GetAsync($"/Institutions/Combined?{ciks}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCombined_ThreeFunds_OrdersConsensusPickFirst()
    {
        // AAPL held by all three funds → should rank first.
        // MSFT held by two of three → second.
        // NVDA held by one → third.
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var nvdaId = Guid.NewGuid();
        var (cikA, cikB, cikC) = ("0003000001", "0003000002", "0003000003");
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var date = new DateOnly(2024, 12, 31);

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
                },
                new CommonStock
                {
                    Id = nvdaId,
                    Ticker = "NVDA",
                    Name = "NVIDIA Corp.",
                    Cik = "0001045810",
                }
            );
            db.AddRange(
                new InstitutionalHolder
                {
                    Id = idA,
                    Cik = cikA,
                    Name = "Fund A",
                },
                new InstitutionalHolder
                {
                    Id = idB,
                    Cik = cikB,
                    Name = "Fund B",
                },
                new InstitutionalHolder
                {
                    Id = idC,
                    Cik = cikC,
                    Name = "Fund C",
                }
            );
            // AAPL — all three funds.
            db.Add(MakeHolding(aaplId, idA, date, value: 1_000_000));
            db.Add(MakeHolding(aaplId, idB, date, value: 1_500_000));
            db.Add(MakeHolding(aaplId, idC, date, value: 2_000_000));
            // MSFT — funds A and B only.
            db.Add(MakeHolding(msftId, idA, date, value: 500_000));
            db.Add(MakeHolding(msftId, idB, date, value: 700_000));
            // NVDA — fund C only.
            db.Add(MakeHolding(nvdaId, idC, date, value: 800_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Combined?ciks={cikA}&ciks={cikB}&ciks={cikC}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"combined-summary\"");
        html.Should().Contain("data-testid=\"combined-portfolio-table\"");

        var tableStart = html.IndexOf(
            "data-testid=\"combined-portfolio-table\"",
            StringComparison.Ordinal
        );
        var table = html.Substring(tableStart);
        var aaplIdx = table.IndexOf("AAPL", StringComparison.Ordinal);
        var msftIdx = table.IndexOf("MSFT", StringComparison.Ordinal);
        var nvdaIdx = table.IndexOf("NVDA", StringComparison.Ordinal);
        aaplIdx.Should().BeLessThan(msftIdx);
        msftIdx.Should().BeLessThan(nvdaIdx);
    }

    [Fact]
    public async Task GetCombined_UnknownCik_ReportsMissing()
    {
        var cikA = "0003000004";
        var idA = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new InstitutionalHolder
                {
                    Id = idA,
                    Cik = cikA,
                    Name = "Fund A",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync(
            $"/Institutions/Combined?ciks={cikA}&ciks=does-not-exist"
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
