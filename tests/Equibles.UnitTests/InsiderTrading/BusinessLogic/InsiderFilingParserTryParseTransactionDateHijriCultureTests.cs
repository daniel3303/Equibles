using System.Globalization;
using Equibles.InsiderTrading.BusinessLogic;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserTryParseTransactionDateHijriCultureTests
{
    // Contract (doc-comment): Form 4 transactionDate is ISO yyyy-MM-dd and must parse
    // culture-independently. Under a non-Gregorian host culture (ar-SA Umm al-Qura) a
    // culture-sensitive parse would fail and silently drop every insider transaction. So the
    // invariant parse must still recover 2024-03-15 as a Gregorian date. Oracle from the contract.
    [Fact]
    public void TryParseTransactionDate_IsoDateUnderUmmAlQuraCulture_ParsesGregorianDate()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var parsed = InsiderFilingParser.TryParseTransactionDate("2024-03-15", out var date);

            parsed.Should().BeTrue();
            date.Should().Be(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
