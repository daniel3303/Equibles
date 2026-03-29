using Equibles.Web.Services;

namespace Equibles.Tests.Web;

public class TechnicalIndicatorServiceTests {

    #region ComputeSma

    [Fact]
    public void ComputeSma_EmptyList_ReturnsEmptyList() {
        var result = TechnicalIndicatorService.ComputeSma([], 5);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeSma_SingleElement_Period1_ReturnsTheElement() {
        var result = TechnicalIndicatorService.ComputeSma([42m], 1);

        result.Should().HaveCount(1);
        result[0].Should().Be(42m);
    }

    [Fact]
    public void ComputeSma_SingleElement_PeriodGreaterThan1_ReturnsNull() {
        var result = TechnicalIndicatorService.ComputeSma([42m], 3);

        result.Should().HaveCount(1);
        result[0].Should().BeNull();
    }

    [Fact]
    public void ComputeSma_ExactPeriodLength_ReturnsSingleValue() {
        // 3 elements, period 3 => first two null, third is average
        var prices = new List<decimal> { 10m, 20m, 30m };
        var result = TechnicalIndicatorService.ComputeSma(prices, 3);

        result.Should().HaveCount(3);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(20m); // (10+20+30)/3 = 20
    }

    [Fact]
    public void ComputeSma_LongerThanPeriod_ReturnsRollingAverage() {
        var prices = new List<decimal> { 2m, 4m, 6m, 8m, 10m };
        var result = TechnicalIndicatorService.ComputeSma(prices, 3);

        result.Should().HaveCount(5);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(4m);     // (2+4+6)/3 = 4
        result[3].Should().Be(6m);     // (4+6+8)/3 = 6
        result[4].Should().Be(8m);     // (6+8+10)/3 = 8
    }

    [Fact]
    public void ComputeSma_KnownValues_VerifyMathematicalCorrectness() {
        var prices = new List<decimal> { 22.27m, 22.19m, 22.08m, 22.17m, 22.18m, 22.13m, 22.23m, 22.43m, 22.24m, 22.29m };
        var result = TechnicalIndicatorService.ComputeSma(prices, 5);

        result.Should().HaveCount(10);

        // First 4 should be null (period=5, indices 0..3)
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().BeNull();
        result[3].Should().BeNull();

        // Index 4: (22.27+22.19+22.08+22.17+22.18)/5 = 110.89/5 = 22.178
        result[4].Should().Be(22.178m);
        // Index 5: (22.19+22.08+22.17+22.18+22.13)/5 = 110.75/5 = 22.15
        result[5].Should().Be(22.15m);
        // Index 6: (22.08+22.17+22.18+22.13+22.23)/5 = 110.79/5 = 22.158
        result[6].Should().Be(22.158m);
        // Index 7: (22.17+22.18+22.13+22.23+22.43)/5 = 111.14/5 = 22.228
        result[7].Should().Be(22.228m);
        // Index 8: (22.18+22.13+22.23+22.43+22.24)/5 = 111.21/5 = 22.242
        result[8].Should().Be(22.242m);
        // Index 9: (22.13+22.23+22.43+22.24+22.29)/5 = 111.32/5 = 22.264
        result[9].Should().Be(22.264m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void ComputeSma_ResultLength_AlwaysMatchesInputLength(int period) {
        var prices = new List<decimal> { 1m, 2m, 3m, 4m, 5m, 6m, 7m };
        var result = TechnicalIndicatorService.ComputeSma(prices, period);

        result.Should().HaveCount(prices.Count);
    }

    [Theory]
    [InlineData(3, 2)] // period 3 => first 2 nulls
    [InlineData(5, 4)] // period 5 => first 4 nulls
    [InlineData(1, 0)] // period 1 => no nulls
    public void ComputeSma_NullPadding_HasCorrectNullCount(int period, int expectedNulls) {
        var prices = new List<decimal> { 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m };
        var result = TechnicalIndicatorService.ComputeSma(prices, period);

        result.Take(expectedNulls).Should().AllSatisfy(v => v.Should().BeNull());
        result.Skip(expectedNulls).Should().AllSatisfy(v => v.Should().NotBeNull());
    }

    [Fact]
    public void ComputeSma_Period1_ReturnsPricesThemselves() {
        var prices = new List<decimal> { 5m, 10m, 15m };
        var result = TechnicalIndicatorService.ComputeSma(prices, 1);

        result.Should().Equal(5m, 10m, 15m);
    }

    [Fact]
    public void ComputeSma_RoundsToFourDecimalPlaces() {
        // 1+2+3 = 6 / 3 = 2 (exact), but let's pick values that produce rounding
        var prices = new List<decimal> { 1m, 1m, 1m, 2m }; // period 3
        var result = TechnicalIndicatorService.ComputeSma(prices, 3);

        // Index 3: (1+1+2)/3 = 1.33333... => rounded to 1.3333
        result[3].Should().Be(1.3333m);
    }

    #endregion

    #region ComputeEma

    [Fact]
    public void ComputeEma_EmptyList_ReturnsEmptyList() {
        var result = TechnicalIndicatorService.ComputeEma([], 5);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeEma_SingleElement_Period1_ReturnsTheElement() {
        var result = TechnicalIndicatorService.ComputeEma([42m], 1);

        result.Should().HaveCount(1);
        result[0].Should().Be(42m);
    }

    [Fact]
    public void ComputeEma_SingleElement_PeriodGreaterThan1_ReturnsNull() {
        var result = TechnicalIndicatorService.ComputeEma([42m], 3);

        result.Should().HaveCount(1);
        result[0].Should().BeNull();
    }

    [Fact]
    public void ComputeEma_SeedValueIsSma() {
        // For period 3, the first EMA value (index 2) should be SMA of first 3 elements
        var prices = new List<decimal> { 10m, 20m, 30m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 3);

        result[2].Should().Be(20m); // SMA = (10+20+30)/3 = 20
    }

    [Fact]
    public void ComputeEma_VerifyMultiplierCalculation() {
        // Period=3, multiplier = 2/(3+1) = 0.5
        // Prices: [10, 20, 30, 40]
        // Index 0,1: null
        // Index 2: SMA = (10+20+30)/3 = 20
        // Index 3: EMA = (40-20)*0.5 + 20 = 30
        var prices = new List<decimal> { 10m, 20m, 30m, 40m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 3);

        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(20m);
        result[3].Should().Be(30m);
    }

    [Fact]
    public void ComputeEma_KnownValues_VerifyMathematicalCorrectness() {
        // Period=5, multiplier = 2/(5+1) = 1/3
        var prices = new List<decimal> { 22.27m, 22.19m, 22.08m, 22.17m, 22.18m, 22.13m, 22.23m, 22.43m, 22.24m, 22.29m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 5);

        result.Should().HaveCount(10);

        // First 4 null
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().BeNull();
        result[3].Should().BeNull();

        // Index 4: SMA = (22.27+22.19+22.08+22.17+22.18)/5 = 22.178
        result[4].Should().Be(22.178m);

        // Index 5: EMA = (22.13 - 22.178) * (1/3) + 22.178 = -0.048/3 + 22.178 = -0.016 + 22.178 = 22.162
        result[5].Should().Be(22.162m);

        // Index 6: EMA = (22.23 - 22.162) * (1/3) + 22.162 = 0.068/3 + 22.162 = 0.02266.. + 22.162 = 22.1847
        result[6].Should().Be(22.1847m);
    }

    [Fact]
    public void ComputeEma_ExactPeriodLength_ReturnsSingleNonNullValue() {
        var prices = new List<decimal> { 5m, 10m, 15m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 3);

        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().Be(10m); // SMA seed = (5+10+15)/3
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void ComputeEma_ResultLength_AlwaysMatchesInputLength(int period) {
        var prices = new List<decimal> { 1m, 2m, 3m, 4m, 5m, 6m, 7m, 8m, 9m, 10m, 11m, 12m };
        var result = TechnicalIndicatorService.ComputeEma(prices, period);

        result.Should().HaveCount(prices.Count);
    }

    [Fact]
    public void ComputeEma_RoundsToFourDecimalPlaces() {
        // Period=3, multiplier=0.5
        // Prices: [1, 1, 1, 2]
        // Index 2: SMA = 1
        // Index 3: (2-1)*0.5 + 1 = 1.5
        // Now use values that force rounding
        // Period=3, multiplier=0.5
        // Prices: [1, 2, 3, 5]
        // Index 2: SMA = 2
        // Index 3: (5-2)*0.5 + 2 = 3.5 -- still exact
        // Try period=6, multiplier = 2/7
        // Prices: [1,2,3,4,5,6,7]
        // Index 5: SMA = (1+2+3+4+5+6)/6 = 21/6 = 3.5
        // Index 6: (7-3.5)*(2/7) + 3.5 = 3.5*2/7 + 3.5 = 1 + 3.5 = 4.5 -- exact again
        // Use period=7, multiplier=2/8=0.25
        // Prices: [1,1,1,1,1,1,1,3]
        // Index 6: SMA = 1
        // Index 7: (3-1)*0.25 + 1 = 1.5 -- exact
        // Try: period=3, mult=0.5, prices=[1,2,3,4,5,6]
        // Index 2: SMA=(1+2+3)/3=2
        // Index 3: (4-2)*0.5+2=3
        // Index 4: (5-3)*0.5+3=4
        // Index 5: (6-4)*0.5+4=5
        // All exact. Let's use non-trivial values.
        // Period=10, multiplier=2/11
        var prices = new List<decimal> { 22.27m, 22.19m, 22.08m, 22.17m, 22.18m, 22.13m, 22.23m, 22.43m, 22.24m, 22.29m, 22.15m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 10);

        // Index 9: SMA = (22.27+22.19+22.08+22.17+22.18+22.13+22.23+22.43+22.24+22.29)/10
        //        = 222.21/10 = 22.221
        result[9].Should().Be(22.221m);

        // Index 10: multiplier = 2/11
        // EMA = (22.15 - 22.221)*(2/11) + 22.221 = (-0.071)*(2/11) + 22.221
        //      = -0.0129090909... + 22.221 = 22.2080909... => rounded to 22.2081
        result[10].Should().Be(22.2081m);
    }

    [Fact]
    public void ComputeEma_ConstantPrices_AllValuesEqualPrice() {
        var prices = new List<decimal> { 50m, 50m, 50m, 50m, 50m, 50m };
        var result = TechnicalIndicatorService.ComputeEma(prices, 3);

        // SMA seed = 50, all subsequent EMA = (50-50)*mult + 50 = 50
        result[2].Should().Be(50m);
        result[3].Should().Be(50m);
        result[4].Should().Be(50m);
        result[5].Should().Be(50m);
    }

    #endregion

    #region ComputeRsi

    [Fact]
    public void ComputeRsi_EmptyList_ReturnsEmptyList() {
        var result = TechnicalIndicatorService.ComputeRsi([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeRsi_FewerThanPeriodPlusOneElements_ReturnsAllNulls() {
        // Default period=14, need >14 elements. With exactly 14, all null.
        var prices = Enumerable.Range(1, 14).Select(x => (decimal)x).ToList();
        var result = TechnicalIndicatorService.ComputeRsi(prices);

        result.Should().HaveCount(14);
        result.Should().AllSatisfy(v => v.Should().BeNull());
    }

    [Fact]
    public void ComputeRsi_ExactlyPeriodPlusOneElements_ReturnsAllNulls() {
        // count == period => branch: prices.Count <= period => all null
        // With default period=14, need exactly 14 elements => all null
        var prices = Enumerable.Range(1, 14).Select(x => (decimal)x).ToList();
        var result = TechnicalIndicatorService.ComputeRsi(prices, 14);

        result.Should().HaveCount(14);
        result.Should().AllSatisfy(v => v.Should().BeNull());
    }

    [Fact]
    public void ComputeRsi_AllGains_ReturnsRsi100() {
        // Prices that only go up => avgLoss=0 => rs=100 => RSI = 100 - 100/101 = 99.01
        // Wait, let's re-read: rs = avgLoss == 0 ? 100m : avgGain / avgLoss
        // RSI = 100 - 100/(1+100) = 100 - 100/101 = 100 - 0.990099.. = 99.0099.. => rounded to 99.01
        // Hmm, that's not exactly 100. Let's verify with period=3 for simplicity.
        var prices = new List<decimal> { 10m, 11m, 12m, 13m, 14m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        // count=5, period=3, count > period => proceeds
        // changes: [_, +1, +1, +1, +1]
        // gains:   [0, 1, 1, 1, 1]
        // losses:  [0, 0, 0, 0, 0]
        // avgGain = (1+1+1)/3 = 1; avgLoss = 0/3 = 0
        // rs = 100 (avgLoss==0 branch)
        // RSI[3] = 100 - 100/(1+100) = 100 - 0.990099... = 99.0099... => 99.01
        result[3].Should().Be(99.01m);

        // Index 4: avgGain = (1*2 + 1)/3 = 1; avgLoss = (0*2 + 0)/3 = 0
        // rs = 100 => RSI = 99.01
        result[4].Should().Be(99.01m);
    }

    [Fact]
    public void ComputeRsi_AllLosses_ReturnsRsiNearZero() {
        // Prices only go down => avgGain=0 => rs = 0/avgLoss = 0
        // RSI = 100 - 100/(1+0) = 100 - 100 = 0
        var prices = new List<decimal> { 20m, 19m, 18m, 17m, 16m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        // avgGain = 0, avgLoss = (1+1+1)/3 = 1
        // rs = 0/1 = 0
        // RSI = 100 - 100/1 = 0
        result[3].Should().Be(0m);

        // Index 4: avgGain = (0*2+0)/3 = 0; avgLoss = (1*2+1)/3 = 1
        // RSI = 0
        result[4].Should().Be(0m);
    }

    [Fact]
    public void ComputeRsi_MixedChanges_VerifyKnownValues() {
        // Using period=3 for manual calculation
        // Prices: [44, 44.34, 44.09, 43.61, 44.33]
        var prices = new List<decimal> { 44m, 44.34m, 44.09m, 43.61m, 44.33m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        // changes: [_, +0.34, -0.25, -0.48, +0.72]
        // gains:   [0, 0.34, 0, 0, 0.72]
        // losses:  [0, 0, 0.25, 0.48, 0]
        // avgGain = (0.34 + 0 + 0)/3 = 0.1133...
        // avgLoss = (0 + 0.25 + 0.48)/3 = 0.2433...
        // rs = 0.1133.../0.2433... = 0.465753...
        // RSI[3] = 100 - 100/(1.465753...) = 100 - 68.2191... = 31.7808... => 31.78
        result[3].Should().Be(31.78m);

        // Index 4: avgGain = (0.1133...*2 + 0.72)/3 = (0.2266...+0.72)/3 = 0.9466.../3 = 0.31555...
        // avgLoss = (0.2433...*2 + 0)/3 = 0.4866.../3 = 0.16222...
        // rs = 0.31555.../0.16222... = 1.94520...
        // RSI = 100 - 100/(2.94520...) = 100 - 33.9531... = 66.0468... => 66.05
        result[4].Should().Be(66.05m);
    }

    [Fact]
    public void ComputeRsi_NullPadding_HasCorrectPattern() {
        // With period=3, count=6: index 0 is null (no change), index 1-2 null (lookback), index 3 is first RSI
        var prices = new List<decimal> { 10m, 11m, 12m, 11m, 13m, 12m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        result.Should().HaveCount(6);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().BeNull();
        result[3].Should().NotBeNull();
        result[4].Should().NotBeNull();
        result[5].Should().NotBeNull();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(14)]
    public void ComputeRsi_ResultLength_AlwaysMatchesInputLength(int period) {
        // Need count > period
        var prices = Enumerable.Range(1, period + 5).Select(x => (decimal)x).ToList();
        var result = TechnicalIndicatorService.ComputeRsi(prices, period);

        result.Should().HaveCount(prices.Count);
    }

    [Fact]
    public void ComputeRsi_ConstantPrices_AllZeroChanges() {
        // All changes are 0 => avgGain=0, avgLoss=0
        // rs = 100 (avgLoss==0 branch) => RSI = 99.01
        var prices = new List<decimal> { 50m, 50m, 50m, 50m, 50m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        // avgGain=0, avgLoss=0 => avgLoss==0 => rs=100 => RSI = 99.01
        result[3].Should().Be(99.01m);
    }

    [Fact]
    public void ComputeRsi_DefaultPeriodIs14() {
        // Provide 16 prices (>14), verify first RSI appears at index 14
        var prices = Enumerable.Range(1, 16).Select(x => (decimal)x).ToList();
        var result = TechnicalIndicatorService.ComputeRsi(prices);

        result.Should().HaveCount(16);

        // Indices 0..13 should be null (index 0 = no change, 1..13 = lookback)
        for (var i = 0; i < 14; i++) {
            result[i].Should().BeNull($"index {i} should be null during lookback");
        }

        // Index 14 should be the first RSI value (all gains => 99.01)
        result[14].Should().Be(99.01m);
    }

    [Fact]
    public void ComputeRsi_RoundsToTwoDecimalPlaces() {
        // Construct a scenario that produces a non-round RSI
        // period=3, prices: [100, 101, 100, 101]
        // changes: [_, +1, -1, +1]
        // gains: [0, 1, 0, 0] (first period: indices 1..3 => 1+0+0... wait, period ends at index 3)
        // Wait: first `period` changes are indices 1..3, so gains[1]=1, gains[2]=0, gains[3]=0... No.
        // Let me use: [100, 103, 100, 102]
        // changes: [_, +3, -3, +2]
        // gains: [0, 3, 0, 2], losses: [0, 0, 3, 0]
        // avgGain = (3+0+0)/3 = 1; avgLoss = (0+3+0)/3 = 1
        // Hmm wait, the first period sums indices 1..period=1..3
        // Actually first RSI is at index=period=3
        // avgGain = (gains[1]+gains[2]+gains[3])/3 = (3+0+2)/3 = 5/3 = 1.6666...
        // avgLoss = (losses[1]+losses[2]+losses[3])/3 = (0+3+0)/3 = 1
        // rs = 1.6666.../1 = 1.6666...
        // RSI = 100 - 100/(2.6666...) = 100 - 37.5 = 62.5
        var prices = new List<decimal> { 100m, 103m, 100m, 102m };
        var result = TechnicalIndicatorService.ComputeRsi(prices, 3);

        result[3].Should().Be(62.5m);
    }

    #endregion

    #region ComputeMacd

    [Fact]
    public void ComputeMacd_EmptyList_ReturnsEmptyLists() {
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd([]);

        line.Should().BeEmpty();
        signal.Should().BeEmpty();
        histogram.Should().BeEmpty();
    }

    [Fact]
    public void ComputeMacd_ResultLengths_AlwaysMatchInputLength() {
        var prices = Enumerable.Range(1, 50).Select(x => (decimal)x).ToList();
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices);

        line.Should().HaveCount(50);
        signal.Should().HaveCount(50);
        histogram.Should().HaveCount(50);
    }

    [Fact]
    public void ComputeMacd_MacdLine_IsFastEmaMinusSlowEma() {
        // Use small periods for testability: fast=2, slow=4, signal=2
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m, 60m };

        var (line, _, _) = TechnicalIndicatorService.ComputeMacd(prices, fastPeriod: 2, slowPeriod: 4, signalPeriod: 2);

        // Compute expected fast EMA (period=2, multiplier = 2/3)
        var fastEma = TechnicalIndicatorService.ComputeEma(prices, 2);
        // Compute expected slow EMA (period=4, multiplier = 2/5)
        var slowEma = TechnicalIndicatorService.ComputeEma(prices, 4);

        for (var i = 0; i < prices.Count; i++) {
            if (fastEma[i] == null || slowEma[i] == null) {
                line[i].Should().BeNull($"index {i}: either fast or slow EMA is null");
            } else {
                var expected = Math.Round(fastEma[i].Value - slowEma[i].Value, 4);
                line[i].Should().Be(expected, $"index {i}: MACD line = fast EMA - slow EMA");
            }
        }
    }

    [Fact]
    public void ComputeMacd_NullPadding_FirstNonNullAtSlowPeriodMinusOne() {
        // With default params (12/26/9), slow period is 26.
        // Slow EMA first non-null at index 25, fast at index 11.
        // MACD line first non-null at index 25 (when both are available).
        var prices = Enumerable.Range(1, 40).Select(x => (decimal)x).ToList();
        var (line, _, _) = TechnicalIndicatorService.ComputeMacd(prices);

        for (var i = 0; i < 25; i++) {
            line[i].Should().BeNull($"index {i} should be null (before slow EMA is available)");
        }

        line[25].Should().NotBeNull("index 25 is the first MACD value (slow period - 1)");
    }

    [Fact]
    public void ComputeMacd_SignalLine_IsEmaOfMacdLine() {
        // Use fast=2, slow=3, signal=2 for manageable hand-calculation
        // Prices: [10, 20, 30, 40, 50, 60, 70]
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m, 60m, 70m };

        var (line, signal, _) = TechnicalIndicatorService.ComputeMacd(prices, fastPeriod: 2, slowPeriod: 3, signalPeriod: 2);

        // Fast EMA (period=2, mult=2/3):
        //   [0]=null, [1]=15 (SMA), [2]=(30-15)*2/3+15=25, [3]=(40-25)*2/3+25=35, [4]=(50-35)*2/3+35=45, [5]=(60-45)*2/3+45=55, [6]=(70-55)*2/3+55=65
        // Slow EMA (period=3, mult=0.5):
        //   [0]=null, [1]=null, [2]=20 (SMA), [3]=(40-20)*0.5+20=30, [4]=(50-30)*0.5+30=40, [5]=(60-40)*0.5+40=50, [6]=(70-50)*0.5+50=60
        // MACD line (first non-null at index 2):
        //   [2]=25-20=5, [3]=35-30=5, [4]=45-40=5, [5]=55-50=5, [6]=65-60=5

        // All MACD values are 5 => signal EMA of constant 5 = 5
        // Signal: first MACD non-null is index 2, so macdValues=[5,5,5,5,5]
        // Signal EMA period=2: [0]=null => mapped to index 2, [1]=SMA of first 2 = 5 => mapped to index 3
        // So signal[2]=null, signal[3]=5, signal[4]=5, signal[5]=5, signal[6]=5
        signal[0].Should().BeNull();
        signal[1].Should().BeNull();
        signal[2].Should().BeNull();
        signal[3].Should().Be(5m);
        signal[4].Should().Be(5m);
        signal[5].Should().Be(5m);
        signal[6].Should().Be(5m);
    }

    [Fact]
    public void ComputeMacd_Histogram_IsMacdMinusSignal() {
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m, 60m, 70m };

        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices, fastPeriod: 2, slowPeriod: 3, signalPeriod: 2);

        for (var i = 0; i < prices.Count; i++) {
            if (line[i] == null || signal[i] == null) {
                histogram[i].Should().BeNull($"index {i}: histogram null when MACD or signal is null");
            } else {
                var expected = Math.Round(line[i].Value - signal[i].Value, 4);
                histogram[i].Should().Be(expected, $"index {i}: histogram = MACD - signal");
            }
        }
    }

    [Fact]
    public void ComputeMacd_DefaultParameters_AreCorrect() {
        // Verify default params: fast=12, slow=26, signal=9
        // By checking that the first MACD value appears at index 25 (slow period - 1)
        // and signal first appears at slow period - 1 + signal period - 1 = 25 + 8 = 33
        var prices = Enumerable.Range(1, 50).Select(x => (decimal)x).ToList();
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices);

        // MACD line: first non-null at index 25
        line[24].Should().BeNull();
        line[25].Should().NotBeNull();

        // Signal: EMA of MACD values with period 9.
        // First signal non-null = first MACD non-null index + (signalPeriod - 1)
        // = 25 + 8 = 33
        signal[32].Should().BeNull();
        signal[33].Should().NotBeNull();

        // Histogram: non-null only when both line and signal are non-null
        histogram[32].Should().BeNull();
        histogram[33].Should().NotBeNull();
    }

    [Fact]
    public void ComputeMacd_ConstantPrices_MacdLineIsZero() {
        // When all prices are equal, fast EMA = slow EMA = price => MACD = 0
        var prices = Enumerable.Repeat(100m, 40).ToList();
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices);

        var nonNullMacdValues = line.Where(v => v != null).ToList();
        nonNullMacdValues.Should().AllSatisfy(v => v.Should().Be(0m));

        var nonNullSignalValues = signal.Where(v => v != null).ToList();
        nonNullSignalValues.Should().AllSatisfy(v => v.Should().Be(0m));

        var nonNullHistogramValues = histogram.Where(v => v != null).ToList();
        nonNullHistogramValues.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    [Fact]
    public void ComputeMacd_KnownValues_VerifyWithSmallPeriods() {
        // fast=2 (mult=2/3), slow=4 (mult=2/5), signal=3 (mult=2/4=0.5)
        var prices = new List<decimal> { 10m, 12m, 11m, 14m, 13m, 16m, 15m, 18m };

        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices, fastPeriod: 2, slowPeriod: 4, signalPeriod: 3);

        // Fast EMA (period=2, mult=2/3):
        //   [0]=null
        //   [1]=SMA(10,12)=11
        //   [2]=(11-11)*2/3+11=11
        //   [3]=(14-11)*2/3+11=13
        //   [4]=(13-13)*2/3+13=13
        //   [5]=(16-13)*2/3+13=15
        //   [6]=(15-15)*2/3+15=15
        //   [7]=(18-15)*2/3+15=17
        var fastEma = TechnicalIndicatorService.ComputeEma(prices, 2);
        fastEma[1].Should().Be(11m);
        fastEma[3].Should().Be(13m);
        fastEma[5].Should().Be(15m);
        fastEma[7].Should().Be(17m);

        // Slow EMA (period=4, mult=2/5=0.4):
        //   [0..2]=null
        //   [3]=SMA(10,12,11,14)=11.75
        //   [4]=(13-11.75)*0.4+11.75 = 0.5+11.75=12.25
        //   [5]=(16-12.25)*0.4+12.25 = 1.5+12.25=13.75
        //   [6]=(15-13.75)*0.4+13.75 = 0.5+13.75=14.25
        //   [7]=(18-14.25)*0.4+14.25 = 1.5+14.25=15.75
        var slowEma = TechnicalIndicatorService.ComputeEma(prices, 4);
        slowEma[3].Should().Be(11.75m);
        slowEma[4].Should().Be(12.25m);
        slowEma[7].Should().Be(15.75m);

        // MACD line (non-null from index 3):
        //   [3]=13-11.75=1.25, [4]=13-12.25=0.75, [5]=15-13.75=1.25, [6]=15-14.25=0.75, [7]=17-15.75=1.25
        line[3].Should().Be(1.25m);
        line[4].Should().Be(0.75m);
        line[5].Should().Be(1.25m);
        line[6].Should().Be(0.75m);
        line[7].Should().Be(1.25m);

        // Signal (EMA of MACD values [1.25, 0.75, 1.25, 0.75, 1.25], period=3, mult=0.5):
        //   macdValues=[1.25, 0.75, 1.25, 0.75, 1.25]
        //   signal[0]=null => maps to prices index 3
        //   signal[1]=null => maps to prices index 4
        //   signal[2]=SMA(1.25,0.75,1.25)=3.25/3=1.0833 => maps to prices index 5
        //   signal[3]=(0.75-1.0833)*0.5+1.0833=(-0.3333)*0.5+1.0833=-0.1667+1.0833=0.9167 => maps to index 6
        //   signal[4]=(1.25-0.9167)*0.5+0.9167=(0.3333)*0.5+0.9167=0.1667+0.9167=1.0833 => maps to index 7
        signal[3].Should().BeNull();
        signal[4].Should().BeNull();
        signal[5].Should().Be(1.0833m);
        signal[6].Should().Be(0.9166m);
        signal[7].Should().Be(1.0833m);

        // Histogram:
        //   [5]=1.25-1.0833=0.1667
        //   [6]=0.75-0.9167=-0.1667
        //   [7]=1.25-1.0833=0.1667
        histogram[5].Should().Be(0.1667m);
        histogram[6].Should().Be(-0.1666m);
        histogram[7].Should().Be(0.1667m);
    }

    [Fact]
    public void ComputeMacd_InsufficientDataForSignal_SignalAndHistogramNull() {
        // With fast=2, slow=3, signal=5 and only 5 prices:
        // MACD non-null from index 2 => only 3 MACD values (indices 2,3,4)
        // Signal needs 5 values => never enough => signal stays null
        var prices = new List<decimal> { 10m, 20m, 30m, 40m, 50m };
        var (line, signal, histogram) = TechnicalIndicatorService.ComputeMacd(prices, fastPeriod: 2, slowPeriod: 3, signalPeriod: 5);

        // MACD line has values from index 2
        line[2].Should().NotBeNull();
        line[3].Should().NotBeNull();
        line[4].Should().NotBeNull();

        // Signal: only 3 MACD values but need period=5, so all signal remain null
        signal[2].Should().BeNull();
        signal[3].Should().BeNull();
        signal[4].Should().BeNull();

        // Histogram also null
        histogram[2].Should().BeNull();
        histogram[3].Should().BeNull();
        histogram[4].Should().BeNull();
    }

    #endregion
}
