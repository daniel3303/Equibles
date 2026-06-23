using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// Pins the inclusive lower bracket of
/// <see cref="InsiderFilingParser.IsPlausibleTransactionDate"/>: the year floor is
/// <c>MinPlausibleTransactionYear</c> (1900) and the bound is inclusive, so a
/// transaction dated exactly on 1900 (and on/before the filing date) is plausible.
/// Existing tests cover the filing-date upper edge and far-past anchoring (year 0022)
/// but not the exact floor — the off-by-one spot (<c>&gt;=</c> vs <c>&gt;</c>).
/// </summary>
public class InsiderFilingParserIsPlausibleTransactionDateYearFloorTests
{
    [Fact]
    public void IsPlausibleTransactionDate_TransactionInFloorYear_IsPlausible()
    {
        var atFloor = new DateOnly(1900, 1, 1);
        var filingDate = new DateOnly(2025, 1, 13);

        InsiderFilingParser.IsPlausibleTransactionDate(atFloor, filingDate).Should().BeTrue();
    }
}
