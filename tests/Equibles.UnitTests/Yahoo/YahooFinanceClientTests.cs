using System.Reflection;
using Equibles.Integrations.Yahoo;

namespace Equibles.UnitTests.Yahoo;

public class YahooFinanceClientTests {
    private static readonly MethodInfo ToUnixTimestampMethod = typeof(YahooFinanceClient)
        .GetMethod("ToUnixTimestamp", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ToUnixTimestamp_KnownUtcDate_ReturnsCorrectUnixSeconds() {
        // GetHistoricalPrices builds the chart URL with `?period1={ToUnixTimestamp(start)}`,
        // and Yahoo treats those parameters as Unix epoch seconds in UTC. ToUnixTimestamp
        // anchors the conversion by explicitly constructing the DateTime with
        // DateTimeKind.Utc — drop the kind and the subtraction against the UTC-offset
        // UnixEpoch silently uses the host's local timezone, shifting every requested
        // range by the local offset and pulling the wrong day's prices. Pin the UTC
        // anchor by asserting the exact epoch seconds for a known calendar date.
        var result = (long)ToUnixTimestampMethod.Invoke(null, [new DateOnly(2024, 1, 1)]);

        result.Should().Be(1704067200L);
    }


    [Fact]
    public async Task GetHistoricalPrices_NullTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices(null, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoricalPrices_EmptyTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices("", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoricalPrices_WhitespaceTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetHistoricalPrices("   ", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRecommendationTrends_NullTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetRecommendationTrends(null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetRecommendationTrends_EmptyTicker_ThrowsArgumentException() {
        var client = CreateClient();

        var act = () => client.GetRecommendationTrends("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static YahooFinanceClient CreateClient() {
        return new YahooFinanceClient(
            new HttpClient(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<YahooFinanceClient>.Instance);
    }
}
