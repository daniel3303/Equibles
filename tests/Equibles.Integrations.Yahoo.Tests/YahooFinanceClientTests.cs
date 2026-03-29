namespace Equibles.Integrations.Yahoo.Tests;

public class YahooFinanceClientTests {
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
