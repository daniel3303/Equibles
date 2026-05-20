using Equibles.Integrations.Yahoo.Models.Responses;
using Newtonsoft.Json;

namespace Equibles.UnitTests.Yahoo;

public class YahooQuoteSummaryResponseTests
{
    [Fact]
    public void Deserialize_ValidRecommendationTrend_ParsesAllFields()
    {
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "recommendationTrend": {
                    "trend": [
                      { "period": "0m", "strongBuy": 3, "buy": 5, "hold": 8, "sell": 6, "strongSell": 0 },
                      { "period": "-1m", "strongBuy": 2, "buy": 4, "hold": 7, "sell": 5, "strongSell": 1 }
                    ]
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);

        response.QuoteSummary.Should().NotBeNull();
        response.QuoteSummary.Error.Should().BeNull();
        response.QuoteSummary.Result.Should().HaveCount(1);

        var trends = response.QuoteSummary.Result[0].RecommendationTrend.Trend;
        trends.Should().HaveCount(2);

        var current = trends[0];
        current.Period.Should().Be("0m");
        current.StrongBuy.Should().Be(3);
        current.Buy.Should().Be(5);
        current.Hold.Should().Be(8);
        current.Sell.Should().Be(6);
        current.StrongSell.Should().Be(0);
    }

    [Fact]
    public void Deserialize_EmptyResult_ParsesWithEmptyList()
    {
        var json = """
            {
              "quoteSummary": {
                "result": [],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);

        response.QuoteSummary.Result.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_EmptyTrendList_ParsesSuccessfully()
    {
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "recommendationTrend": {
                    "trend": []
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);

        response.QuoteSummary.Result[0].RecommendationTrend.Trend.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_SummaryDetailMarketCap_ParsesRawValue()
    {
        // Captured shape of a real summaryDetail payload: marketCap is a Yahoo "raw"
        // value object. The container reads `raw` as a long so trillion-dollar mega-caps
        // round-trip without overflow.
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "summaryDetail": {
                    "marketCap": { "raw": 2950000000000, "fmt": "2.95T", "longFmt": "2,950,000,000,000" }
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);
        var detail = response.QuoteSummary.Result[0].SummaryDetail;

        detail.MarketCap.Raw.Should().Be(2_950_000_000_000L);
    }

    [Fact]
    public void Deserialize_SummaryDetailMissingMarketCap_LeavesPropertyNull()
    {
        // Yahoo omits keys for issuers without coverage (some ETFs, foreign listings);
        // the container must tolerate the missing marketCap field without throwing.
        var json = """
            {
              "quoteSummary": {
                "result": [{ "summaryDetail": { } }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);
        var detail = response.QuoteSummary.Result[0].SummaryDetail;

        detail.Should().NotBeNull();
        detail.MarketCap.Should().BeNull();
    }

    [Fact]
    public void Deserialize_AssetProfile_ParsesSectorAndIndustryAndCompanyFields()
    {
        // Captured from a real Yahoo quoteSummary?modules=assetProfile response.
        // Confirms the Newtonsoft attribute mapping picks up sector / industry as well
        // as the secondary fields the worker uses for the company profile.
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "assetProfile": {
                    "industry": "Consumer Electronics",
                    "sector": "Technology",
                    "longBusinessSummary": "Apple Inc. designs, manufactures, and markets smartphones, personal computers, tablets, wearables, and accessories worldwide.",
                    "website": "https://www.apple.com"
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);
        var profile = response.QuoteSummary.Result[0].AssetProfile;

        profile.Sector.Should().Be("Technology");
        profile.Industry.Should().Be("Consumer Electronics");
        profile.LongBusinessSummary.Should().StartWith("Apple Inc.");
        profile.Website.Should().Be("https://www.apple.com");
    }

    [Fact]
    public void Deserialize_AssetProfileMissingFields_LeavesPropertiesNull()
    {
        // Yahoo occasionally omits fields when not provided by the issuer (especially
        // for non-US listings or ETFs). The container must tolerate missing keys.
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "assetProfile": {
                    "sector": "Energy"
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);
        var profile = response.QuoteSummary.Result[0].AssetProfile;

        profile.Sector.Should().Be("Energy");
        profile.Industry.Should().BeNull();
        profile.LongBusinessSummary.Should().BeNull();
        profile.Website.Should().BeNull();
    }

    [Fact]
    public void Deserialize_AllZeroCounts_ParsesSuccessfully()
    {
        var json = """
            {
              "quoteSummary": {
                "result": [{
                  "recommendationTrend": {
                    "trend": [
                      { "period": "0w", "strongBuy": 0, "buy": 0, "hold": 0, "sell": 0, "strongSell": 0 }
                    ]
                  }
                }],
                "error": null
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<YahooQuoteSummaryResponse>(json);
        var trend = response.QuoteSummary.Result[0].RecommendationTrend.Trend[0];

        trend.Period.Should().Be("0w");
        trend.StrongBuy.Should().Be(0);
        trend.Buy.Should().Be(0);
        trend.Hold.Should().Be(0);
        trend.Sell.Should().Be(0);
        trend.StrongSell.Should().Be(0);
    }
}
