using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorParseTransactionCultureTests
{
    // Contract: SEC Form 4 transactionDate is ISO yyyy-MM-dd (ownership XSD).
    // ParseTransaction must parse it culture-independently — ar-SA defaults to
    // the Umm al-Qura (Hijri) calendar where culture-sensitive
    // DateOnly.TryParse fails on an ISO string, and ParseTransaction returns
    // null on a failed date parse, so a Worker there silently drops EVERY
    // insider transaction.
    [Fact]
    public void ParseTransaction_IsoTransactionDateUnderHijriCulture_ReturnsTransaction()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var tx = new XElement(
                "transaction",
                new XElement("securityTitle", new XElement("value", "Common Stock")),
                new XElement("transactionDate", new XElement("value", "2024-02-01")),
                new XElement(
                    "transactionAmounts",
                    new XElement("transactionShares", new XElement("value", "100")),
                    new XElement("transactionPricePerShare", new XElement("value", "10")),
                    new XElement("transactionAcquiredDisposedCode", new XElement("value", "A"))
                ),
                new XElement(
                    "postTransactionAmounts",
                    new XElement("sharesOwnedFollowingTransaction", new XElement("value", "100"))
                ),
                new XElement(
                    "ownershipNature",
                    new XElement("directOrIndirectOwnership", new XElement("value", "D"))
                )
            );

            var owner = new InsiderOwner { Id = Guid.NewGuid() };
            var filing = new FilingData
            {
                AccessionNumber = "0000000000-24-000001",
                FilingDate = new DateOnly(2024, 2, 2),
                ReportDate = new DateOnly(2024, 2, 2),
            };

            var method = typeof(InsiderFilingParser).GetMethod(
                "ParseTransaction",
                BindingFlags.NonPublic | BindingFlags.Static
            )!;

            var result = (InsiderTransaction)
                method.Invoke(
                    null,
                    [tx, owner, Guid.NewGuid(), filing, false, InsiderSecurityKind.NonDerivative]
                );

            result.Should().NotBeNull();
            result!.TransactionDate.Should().Be(new DateOnly(2024, 2, 1));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
