using Equibles.Integrations.Yahoo.Models.Responses;
using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Tests;

public class YahooQuoteSummaryResponseTests {
    [Fact]
    public void Deserialize_ValidRecommendationTrend_ParsesAllFields() {
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
    public void Deserialize_EmptyResult_ParsesWithEmptyList() {
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
    public void Deserialize_EmptyTrendList_ParsesSuccessfully() {
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
    public void Deserialize_AllZeroCounts_ParsesSuccessfully() {
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
