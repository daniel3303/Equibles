using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingParserFootnotesTests
{
    // Form 4 references footnotes by id both on the transaction itself and on
    // individual fields (price, shares, ownership). ParseTransactions must resolve
    // every reference within a row to its text, in document order, de-duplicated,
    // ignoring footnotes the row doesn't reference.
    private static List<InsiderTransaction> ParseAll(XElement root)
    {
        var owner = new InsiderOwner { OwnerCik = "0000000001", Name = "Owner" };
        var filing = new FilingData
        {
            AccessionNumber = "0000000000-26-000001",
            Form = "4",
            FilingDate = new DateOnly(2026, 1, 5),
            ReportDate = new DateOnly(2026, 1, 2),
        };
        return InsiderFilingParser.ParseTransactions(root, owner, Guid.NewGuid(), filing, false);
    }

    [Fact]
    public void ParseTransactions_ResolvesPerFieldAndRowFootnotes_InOrderDeduped()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "nonDerivativeTable",
                new XElement(
                    "nonDerivativeTransaction",
                    new XElement("securityTitle", new XElement("value", "Common Stock")),
                    new XElement("transactionDate", new XElement("value", "2026-01-02")),
                    new XElement("transactionCoding", new XElement("transactionCode", "P")),
                    new XElement(
                        "transactionAmounts",
                        new XElement("transactionShares", new XElement("value", "1000")),
                        new XElement(
                            "transactionPricePerShare",
                            new XElement("value", "10"),
                            new XElement("footnoteId", new XAttribute("id", "F1"))
                        )
                    ),
                    // Row-level footnote.
                    new XElement("footnoteId", new XAttribute("id", "F2")),
                    new XElement(
                        "postTransactionAmounts",
                        new XElement(
                            "sharesOwnedFollowingTransaction",
                            new XElement("value", "1000"),
                            // Duplicate reference to F1 — must collapse to one note.
                            new XElement("footnoteId", new XAttribute("id", "F1"))
                        )
                    )
                )
            ),
            new XElement(
                "footnotes",
                new XElement(
                    "footnote",
                    new XAttribute("id", "F1"),
                    "Price is a weighted average."
                ),
                new XElement("footnote", new XAttribute("id", "F2"), "Shares held in a trust."),
                new XElement("footnote", new XAttribute("id", "F3"), "Unreferenced note.")
            )
        );

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(1);
        transactions[0]
            .Notes.Should()
            .Equal("Price is a weighted average.", "Shares held in a trust.");
    }

    [Fact]
    public void ParseTransactions_NoFootnotes_NotesIsEmpty()
    {
        var root = new XElement(
            "ownershipDocument",
            new XElement(
                "nonDerivativeTable",
                new XElement(
                    "nonDerivativeTransaction",
                    new XElement("securityTitle", new XElement("value", "Common Stock")),
                    new XElement("transactionDate", new XElement("value", "2026-01-02")),
                    new XElement("transactionCoding", new XElement("transactionCode", "P")),
                    new XElement(
                        "transactionAmounts",
                        new XElement("transactionShares", new XElement("value", "1000")),
                        new XElement("transactionPricePerShare", new XElement("value", "10"))
                    )
                )
            )
        );

        var transactions = ParseAll(root);

        transactions.Should().HaveCount(1);
        transactions[0].Notes.Should().BeEmpty();
    }
}
