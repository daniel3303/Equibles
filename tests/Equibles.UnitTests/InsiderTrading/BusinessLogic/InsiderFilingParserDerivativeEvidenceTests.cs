using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

// The price validator repairs a derivative total only on the filing's own one-for-one evidence,
// so the parser must read underlyingSecurityShares and conversionOrExercisePrice from the
// derivative table, leave a footnote-only value null, and never invent them for other rows.
public class InsiderFilingParserDerivativeEvidenceTests
{
    private static List<InsiderTransaction> ParseAll(XElement root)
    {
        var owner = new InsiderOwner { OwnerCik = "0000000001", Name = "Owner" };
        var filing = new FilingData
        {
            AccessionNumber = "0001104659-26-097430",
            Form = "4",
            FilingDate = new DateOnly(2026, 8, 20),
            ReportDate = new DateOnly(2026, 8, 18),
        };
        return InsiderFilingParser.ParseTransactions(root, owner, Guid.NewGuid(), filing, false);
    }

    private static XElement Amounts(string shares, string price) =>
        new(
            "transactionAmounts",
            new XElement("transactionShares", new XElement("value", shares)),
            new XElement("transactionPricePerShare", new XElement("value", price)),
            new XElement("transactionAcquiredDisposedCode", new XElement("value", "D"))
        );

    private static XElement DerivativeTransaction(
        XElement exercisePrice,
        XElement underlyingShares
    ) =>
        new(
            "derivativeTransaction",
            new XElement(
                "securityTitle",
                new XElement("value", "Call option (obligation to sell)")
            ),
            exercisePrice,
            new XElement("transactionDate", new XElement("value", "2026-08-18")),
            new XElement("transactionCoding", new XElement("transactionCode", "S")),
            Amounts("183640", "3659027"),
            new XElement(
                "underlyingSecurity",
                new XElement(
                    "underlyingSecurityTitle",
                    new XElement("value", "Class A Common Stock")
                ),
                underlyingShares
            )
        );

    [Fact]
    public void ParseTransactions_DerivativeRow_ReadsUnderlyingSharesAndExercisePrice()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "derivativeTable",
                DerivativeTransaction(
                    new XElement("conversionOrExercisePrice", new XElement("value", "130")),
                    new XElement("underlyingSecurityShares", new XElement("value", "183640"))
                )
            )
        );

        var row = ParseAll(root).Should().ContainSingle().Subject;

        row.SecurityKind.Should().Be(InsiderSecurityKind.Derivative);
        row.Shares.Should().Be(183_640);
        row.PricePerShare.Should().Be(3_659_027m);
        row.UnderlyingShares.Should().Be(183_640);
        row.ExercisePrice.Should().Be(130m);
    }

    [Fact]
    public void ParseTransactions_DerivativeRowWithDecimalUnderlyingShares_TruncatesLikeShares()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "derivativeTable",
                DerivativeTransaction(
                    new XElement("conversionOrExercisePrice", new XElement("value", "130.00")),
                    new XElement("underlyingSecurityShares", new XElement("value", "183640.00"))
                )
            )
        );

        var row = ParseAll(root).Should().ContainSingle().Subject;

        row.UnderlyingShares.Should().Be(183_640);
        row.ExercisePrice.Should().Be(130m);
    }

    [Fact]
    public void ParseTransactions_DerivativeRowWithFootnoteOnlyValues_LeavesEvidenceNull()
    {
        // A footnote reference in place of a value is not a zero strike or a zero share count.
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "derivativeTable",
                DerivativeTransaction(
                    new XElement(
                        "conversionOrExercisePrice",
                        new XElement("footnoteId", new XAttribute("id", "F1"))
                    ),
                    new XElement(
                        "underlyingSecurityShares",
                        new XElement("footnoteId", new XAttribute("id", "F2"))
                    )
                )
            )
        );

        var row = ParseAll(root).Should().ContainSingle().Subject;

        row.UnderlyingShares.Should().BeNull();
        row.ExercisePrice.Should().BeNull();
    }

    [Fact]
    public void ParseTransactions_NonDerivativeRow_LeavesEvidenceNull()
    {
        // Even when a malformed non-derivative row carries the elements, only the derivative table yields evidence.
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "nonDerivativeTable",
                new XElement(
                    "nonDerivativeTransaction",
                    new XElement("securityTitle", new XElement("value", "Class A Common Stock")),
                    new XElement("transactionDate", new XElement("value", "2026-08-18")),
                    new XElement("transactionCoding", new XElement("transactionCode", "S")),
                    Amounts("1000", "108"),
                    new XElement("conversionOrExercisePrice", new XElement("value", "130")),
                    new XElement(
                        "underlyingSecurity",
                        new XElement("underlyingSecurityShares", new XElement("value", "1000"))
                    )
                )
            )
        );

        var row = ParseAll(root).Should().ContainSingle().Subject;

        row.SecurityKind.Should().Be(InsiderSecurityKind.NonDerivative);
        row.UnderlyingShares.Should().BeNull();
        row.ExercisePrice.Should().BeNull();
    }
}
