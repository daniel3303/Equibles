using Equibles.Integrations.Yahoo.Models.Responses;
using Newtonsoft.Json;

namespace Equibles.Integrations.Yahoo.Tests;

public class YahooChartResponseTests {
    private const string SampleChartJson = """
    {
      "chart": {
        "result": [{
          "timestamp": [1585569600, 1585656000, 1585742400],
          "indicators": {
            "quote": [{
              "open": [130.25, 132.50, 135.00],
              "high": [137.98, 134.20, 138.50],
              "low": [129.89, 131.00, 133.50],
              "close": [137.06, 133.75, 137.50],
              "volume": [5765400, 4321000, 6100200]
            }],
            "adjclose": [{
              "adjclose": [136.50, 133.20, 137.00]
            }]
          }
        }],
        "error": null
      }
    }
    """;

    [Fact]
    public void Deserialize_ValidChartJson_ParsesAllFields() {
        var response = JsonConvert.DeserializeObject<YahooChartResponse>(SampleChartJson);

        response.Should().NotBeNull();
        response.Chart.Should().NotBeNull();
        response.Chart.Error.Should().BeNull();
        response.Chart.Result.Should().HaveCount(1);

        var result = response.Chart.Result[0];
        result.Timestamp.Should().HaveCount(3);
        result.Timestamp[0].Should().Be(1585569600);
    }

    [Fact]
    public void Deserialize_ValidChartJson_ParsesQuoteArrays() {
        var response = JsonConvert.DeserializeObject<YahooChartResponse>(SampleChartJson);
        var quote = response.Chart.Result[0].Indicators.Quote[0];

        quote.Open.Should().HaveCount(3);
        quote.Open[0].Should().Be(130.25m);
        quote.High[0].Should().Be(137.98m);
        quote.Low[0].Should().Be(129.89m);
        quote.Close[0].Should().Be(137.06m);
        quote.Volume[0].Should().Be(5765400);
    }

    [Fact]
    public void Deserialize_ValidChartJson_ParsesAdjustedClose() {
        var response = JsonConvert.DeserializeObject<YahooChartResponse>(SampleChartJson);
        var adjClose = response.Chart.Result[0].Indicators.AdjClose[0];

        adjClose.AdjustedClose.Should().HaveCount(3);
        adjClose.AdjustedClose[0].Should().Be(136.50m);
    }

    [Fact]
    public void Deserialize_NullValuesInQuote_ParsesAsNulls() {
        // Yahoo returns null for market holidays or missing data points
        var json = """
        {
          "chart": {
            "result": [{
              "timestamp": [1585569600, 1585656000],
              "indicators": {
                "quote": [{
                  "open": [130.25, null],
                  "high": [137.98, null],
                  "low": [129.89, null],
                  "close": [137.06, null],
                  "volume": [5765400, null]
                }],
                "adjclose": [{
                  "adjclose": [136.50, null]
                }]
              }
            }],
            "error": null
          }
        }
        """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);
        var quote = response.Chart.Result[0].Indicators.Quote[0];

        quote.Close[0].Should().Be(137.06m);
        quote.Close[1].Should().BeNull();
        quote.Volume[1].Should().BeNull();
    }

    [Fact]
    public void Deserialize_EmptyResult_ParsesWithEmptyList() {
        var json = """
        {
          "chart": {
            "result": [],
            "error": null
          }
        }
        """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        response.Chart.Result.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_NoAdjClose_IndicatorsStillParse() {
        var json = """
        {
          "chart": {
            "result": [{
              "timestamp": [1585569600],
              "indicators": {
                "quote": [{
                  "open": [130.25],
                  "high": [137.98],
                  "low": [129.89],
                  "close": [137.06],
                  "volume": [5765400]
                }]
              }
            }],
            "error": null
          }
        }
        """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);
        var result = response.Chart.Result[0];

        result.Indicators.Quote.Should().HaveCount(1);
        result.Indicators.AdjClose.Should().BeEmpty();
    }
}
