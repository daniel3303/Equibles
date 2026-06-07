using Equibles.InsiderTrading.Data.Models;

namespace Equibles.UnitTests.InsiderTrading.Models;

public class InsiderSecurityClassificationStockOptionTitleTests
{
    private static readonly Func<InsiderTransaction, bool> IsShareTransaction =
        InsiderSecurityClassification.IsShareTransaction.Compile();

    // Contract (#3502): the title fallback keeps derivatives off the value boards. A
    // "Stock Option" is a derivative even though its title contains the share word "Stock" —
    // the most common Form 4 derivative title must not slip through on that keyword.
    [Fact]
    public void IsShareTransaction_UnknownWithStockOptionTitle_IsFalse()
    {
        var row = new InsiderTransaction
        {
            SecurityKind = InsiderSecurityKind.Unknown,
            SecurityTitle = "Stock Option (Right to Buy)",
        };

        IsShareTransaction(row).Should().BeFalse();
    }
}
