using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserIsPlausibleTransactionDateOnFilingDateTests
{
    [Fact]
    public void IsPlausibleTransactionDate_TransactionOnFilingDate_IsPlausible()
    {
        // Contract: a Form 4 may be filed the same day as the trade, so a transaction dated
        // exactly on the filing date does not post-date it and stays plausible (inclusive upper
        // bound). Guards <= against regressing to <, which would wrongly anchor same-day trades.
        var sameDay = new DateOnly(2025, 1, 13);

        InsiderFilingParser.IsPlausibleTransactionDate(sameDay, sameDay).Should().BeTrue();
    }
}
