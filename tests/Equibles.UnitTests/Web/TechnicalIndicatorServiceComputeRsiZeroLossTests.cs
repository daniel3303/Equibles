using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceComputeRsiZeroLossTests
{
    // Oracle = the well-known RSI domain rule (Wilder 1978; StockCharts; Investopedia):
    // RSI = 100 - 100/(1+RS), RS = avgGain/avgLoss. Over a window with NO losses,
    // avgLoss = 0 so RS is infinite and RSI is 100 by definition (the overbought
    // extreme). A strictly increasing series has zero losses, so its first
    // computable RSI must be exactly 100. Asserting the contract, not the code.
    [Fact]
    public void ComputeRsi_StrictlyIncreasingSeries_FirstValueIs100()
    {
        var prices = new List<decimal> { 1m, 2m, 3m, 4m, 5m };

        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        result[3].Should().Be(100m, "a window with zero losses has infinite RS, so RSI is 100");
    }
}
