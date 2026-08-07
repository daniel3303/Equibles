using Equibles.Integrations.Yahoo.Models.Responses;
using Newtonsoft.Json;

namespace Equibles.UnitTests.Yahoo;

// A single impossible number must cost that one field, never the whole response.
//
// Production incident (2026-08-05 onwards): the feed served an adjusted close of
// 1.2036464262466904E35 for one ADR listing. decimal tops out near 7.9E28, so binding threw
// mid-parse and the listing lost its open/high/low/close/volume as well — every one of which
// was valid — on every cycle, because the upstream value never self-corrects.
public class YahooChartTolerantNumberParsingTests
{
    // The exact shape and value logged in production, trimmed to one row.
    private const string PoisonedAdjCloseJson = """
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
                }],
                "adjclose": [{
                  "adjclose": [1.2036464262466904E35]
                }]
              }
            }],
            "error": null
          }
        }
        """;

    [Fact]
    public void Deserialize_AdjustedCloseBeyondDecimal_DoesNotThrow()
    {
        var parse = () => JsonConvert.DeserializeObject<YahooChartResponse>(PoisonedAdjCloseJson);

        parse.Should().NotThrow();
    }

    [Fact]
    public void Deserialize_AdjustedCloseBeyondDecimal_YieldsNullForThatFieldOnly()
    {
        var response = JsonConvert.DeserializeObject<YahooChartResponse>(PoisonedAdjCloseJson);

        var result = response.Chart.Result[0];
        result.Indicators.AdjClose[0].AdjustedClose[0].Should().BeNull();

        // The whole point: the rest of the bar survives.
        var quote = result.Indicators.Quote[0];
        quote.Open[0].Should().Be(130.25m);
        quote.High[0].Should().Be(137.98m);
        quote.Low[0].Should().Be(129.89m);
        quote.Close[0].Should().Be(137.06m);
        quote.Volume[0].Should().Be(5765400);
    }

    [Theory]
    [InlineData("1.2036464262466904E35")] // the production value
    [InlineData("-1.2036464262466904E35")] // same magnitude, negative
    [InlineData("1E308")] // near double's ceiling
    [InlineData("\"not-a-number\"")] // a string where a number belongs
    [InlineData("true")] // a wholly wrong token type
    public void Deserialize_UnrepresentablePrice_BecomesNull(string literal)
    {
        var json = $$"""
            { "chart": { "result": [{ "indicators": {
              "quote": [{ "close": [{{literal}}] }] } }] } }
            """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        response.Chart.Result[0].Indicators.Quote[0].Close[0].Should().BeNull();
    }

    [Fact]
    public void Deserialize_VolumeBeyondLong_BecomesNull()
    {
        var json = """
            { "chart": { "result": [{ "indicators": {
              "quote": [{ "volume": [1.2036464262466904E35] }] } }] } }
            """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        response.Chart.Result[0].Indicators.Quote[0].Volume[0].Should().BeNull();
    }

    [Fact]
    public void Deserialize_NullHoles_StayNull()
    {
        // A holiday-edge row: the tolerant read must not turn a legitimate null into a zero.
        var json = """
            { "chart": { "result": [{ "indicators": {
              "quote": [{ "close": [137.06, null, 138.50] }] } }] } }
            """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        var close = response.Chart.Result[0].Indicators.Quote[0].Close;
        close.Should().HaveCount(3);
        close[0].Should().Be(137.06m);
        close[1].Should().BeNull();
        close[2].Should().Be(138.50m);
    }

    [Fact]
    public void Deserialize_OrdinaryPrices_KeepFourDecimalPrecision()
    {
        // Guards the tolerant path against silently coarsening real values.
        var json = """
            { "chart": { "result": [{ "indicators": {
              "quote": [{ "close": [0.0001, 1234.5678, 137.05999755859375] }] } }] } }
            """;

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        var close = response.Chart.Result[0].Indicators.Quote[0].Close;
        close[0].Should().Be(0.0001m);
        close[1].Should().Be(1234.5678m);
        Math.Round(close[2].Value, 4).Should().Be(137.06m);
    }

    [Fact]
    public void Deserialize_MissingArray_IsEmptyNotNull()
    {
        var json = """{ "chart": { "result": [{ "indicators": { "quote": [{}] } }] } }""";

        var response = JsonConvert.DeserializeObject<YahooChartResponse>(json);

        response.Chart.Result[0].Indicators.Quote[0].Close.Should().BeEmpty();
    }
}
