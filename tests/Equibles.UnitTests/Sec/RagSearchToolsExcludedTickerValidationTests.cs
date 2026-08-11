using Equibles.Errors.BusinessLogic;
using Equibles.Sec.Mcp.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class RagSearchToolsExcludedTickerValidationTests
{
    private static RagSearchTools Tool() =>
        new(
            null,
            null,
            null,
            null,
            null,
            new ErrorManager(null),
            NullLogger<RagSearchTools>.Instance
        );

    [Theory]
    [InlineData("ſPY")]
    [InlineData("AAPL/../../x")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    [InlineData("AAPL,,MSFT")]
    public async Task SearchDocuments_InvalidExcludedTickerFailsBeforeSearch(string tickers)
    {
        var result = await Tool().SearchDocuments("revenue", excludeTickers: tickers);

        result.Should().Contain("Invalid excluded ticker");
    }

    [Fact]
    public async Task SearchDocuments_TooManyExcludedTickersFailsBeforeSearch()
    {
        var tickers = string.Join(',', Enumerable.Range(1, 26).Select(index => $"T{index}"));

        var result = await Tool().SearchDocuments("revenue", excludeTickers: tickers);

        result.Should().Contain("Maximum 25 excluded tickers");
    }
}
