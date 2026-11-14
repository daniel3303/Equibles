using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// A stock split between the two compared quarters moves every as-filed share count. The
/// market-wide boards judge buying, selling and doubling down on counts restated to one basis.
/// </summary>
[Collection(WebHostCollection.Name)]
public class HoldingsBoardsSplitBasisTests
{
    private static readonly DateOnly Prior = new(2024, 9, 30);
    private static readonly DateOnly Current = new(2024, 12, 31);
    private static readonly DateOnly SplitDay = new(2024, 11, 15);

    private readonly WebHostFixture _fixture;

    public HoldingsBoardsSplitBasisTests(WebHostFixture fixture) => _fixture = fixture;

    // Counts from a real pair of 13F filings either side of a four-for-one split.
    [Fact]
    public async Task ExportActivity_ForwardSplitBetweenTheQuarters_ReportsTheSharesActuallyBought()
    {
        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(Issuer(stockId, "FWDS", "Forward Split Co.", "0009600001"));
            db.Add(Holder(holderId, "0009600101", "Steady Holder"));
            db.Add(Split(stockId, "FWDS", numerator: 4, denominator: 1));
            db.Add(Holding(stockId, holderId, Prior, 11_583_850));
            db.Add(Holding(stockId, holderId, Current, 46_512_447));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Holdings/Export/Activity");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("FWDS");
        csv.Should().Contain("177047");
        csv.Should().NotContain("34928597");
    }

    [Fact]
    public async Task DoubleDown_SplitsBetweenTheQuarters_JudgeGrowthOnRestatedCounts()
    {
        var forwardId = Guid.NewGuid();
        var reverseId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(Issuer(forwardId, "FWDS", "Forward Split Co.", "0009600001"));
            db.Add(Issuer(reverseId, "RVSS", "Reverse Split Co.", "0009600002"));
            db.Add(Holder(holderId, "0009600101", "Steady Holder"));
            db.Add(Split(forwardId, "FWDS", numerator: 4, denominator: 1));
            db.Add(Split(reverseId, "RVSS", numerator: 1, denominator: 10));
            // Unchanged across the forward split: four times the count, nothing bought.
            db.Add(Holding(forwardId, holderId, Prior, 11_583_850));
            db.Add(Holding(forwardId, holderId, Current, 46_512_447));
            // Doubled across the reverse split: 1,000,000 old shares are 100,000 new ones.
            db.Add(Holding(reverseId, holderId, Prior, 1_000_000));
            db.Add(Holding(reverseId, holderId, Current, 200_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/holdings/double-down-report");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Reverse Split Co.");
        html.Should().NotContain("Forward Split Co.");
    }

    private static EquityIssuer Issuer(Guid id, string ticker, string name, string cik) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(Id: id, Ticker: ticker, Name: name, Cik: cik);

    private static InstitutionalHolder Holder(Guid id, string cik, string name) =>
        new()
        {
            Id = id,
            Cik = cik,
            Name = name,
        };

    private static StockSplit Split(Guid stockId, string ticker, int numerator, int denominator) =>
        new()
        {
            EquityIssuerId = stockId,
            PriceSeriesTicker = ticker,
            EffectiveDate = SplitDay,
            Numerator = numerator,
            Denominator = denominator,
        };

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = 50_000_000,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString("N")[..20],
        };
}
