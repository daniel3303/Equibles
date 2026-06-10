using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Pins IrEventClassifier's precedence on a combined label: the most common
/// real-world event title, "Q3 2025 Earnings Conference Call", matches both
/// the earnings and the conference keywords and must classify as an earnings
/// call — a keyword reordering would silently flip every quarterly call to
/// Conference.
/// </summary>
public class IrEventClassifierClassifyTests
{
    [Fact]
    public void Classify_EarningsConferenceCallTitle_ClassifiesAsEarningsCall()
    {
        var result = IrEventClassifier.Classify("Q3 2025 Earnings Conference Call");

        result.Should().Be(IrEventType.EarningsCall);
    }
}
